using Unity.MLAgents;
using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 학습용 경기장 하나: 경기장 + 에이전트(P1) + 상대 봇(P2).
    /// 규칙은 ArenaGame과 같다 (링아웃, 서든데스, 시간 종료 판정). 화면·입력·카메라만 없다.
    /// 여러 개를 띄워서 한 번에 여러 판을 동시에 학습한다.
    /// </summary>
    public class TrainingArena : MonoBehaviour, IFightContext
    {
        [Header("Match")]
        public float arenaRadius = 5f;
        public float roundTime = 30f;
        public float suddenDeathTime = 12f;
        public float suddenDeathMinRadius = 1f;
        public float fallHeight = -1.5f;
        public float spawnDistance = 2f;

        [Header("Rewards (보상 A)")]
        public float winReward = 1f;
        [Tooltip("둘 다 떨어졌을 때 보상. 음수로 둬서 같이 뛰어내리는 전략을 막는다")]
        public float mutualFallReward = -0.5f;

        readonly RingOutJudge judge = new RingOutJudge();

        public Arena Arena { get; private set; }
        public FighterAgent Agent { get; private set; }
        public FighterController[] Fighters { get; } = new FighterController[2];

        public enum Bot { Rush, Grab, Random }
        /// <summary>판마다 상대 봇을 뽑는 비율 (Rush, Grab, Random)</summary>
        public static float[] BotWeights = { 0.4f, 0.4f, 0.2f };
        /// <summary>평가 때 한 종류만 쓰려면 지정 (null = 섞어서)</summary>
        public static Bot? ForcedBot;
        public Bot CurrentBot { get; private set; }
        public float TimeLeft { get; private set; }

        // 통계 (TensorBoard에 기록)
        int episodes;

        public void Build(Unity.InferenceEngine.ModelAsset model = null)
        {
            Arena = Arena.Create(transform, arenaRadius);
            Color[] colors = { new Color(0.35f, 0.6f, 0.95f), new Color(0.95f, 0.45f, 0.35f) }; // AI = 파랑, 봇 = 빨강 (게임과 같게)
            for (int i = 0; i < 2; i++)
            {
                var rd = Ragdoll.Create($"Fighter{i + 1}", transform.position + Vector3.right * (i == 0 ? -2f : 2f), Quaternion.identity, colors[i], transform);
                var fc = rd.gameObject.AddComponent<FighterController>();
                fc.Init();
                Fighters[i] = fc;
            }

            ResetRound();
            Agent = FighterAgent.Create(transform, this, Fighters[0], Fighters[1], model);
            mutualFallReward = TrainingManager.MutualFallReward;
            if (!TrainingManager.Shaping)
            {
                // 보상 A: 승패 + 시간 벌점만
                Agent.approachReward = 0f;
                Agent.grabReward = 0f;
                Agent.pushOutReward = 0f;
                Agent.safeReturnReward = 0f;
            }
        }

        public void ResetRound()
        {
            CurrentBot = ForcedBot ?? PickBot();
            Fighters[1].InputSource = CurrentBot switch
            {
                Bot.Grab => new GrabBotInput(Fighters[1], Fighters[0], this),
                Bot.Random => new RandomBotInput(Fighters[1]),
                _ => new RushBotInput(Fighters[1], Fighters[0]),
            };

            // 시작 방향·위치를 무작위로 (어느 쪽에서 시작해도 이길 수 있게)
            float angle = Random.Range(0f, 360f);
            Vector3 axis = Quaternion.Euler(0f, angle, 0f) * Vector3.right;
            bool swap = Random.value < 0.5f;
            for (int i = 0; i < 2; i++)
            {
                float side = (i == 0) == swap ? 1f : -1f;
                Vector3 pos = transform.position + axis * side * spawnDistance + Vector3.up * 0.05f;
                var rot = Quaternion.LookRotation(-axis * side, Vector3.up);
                Fighters[i].ResetFighter(pos, rot);
            }
            Arena.SetRadius(arenaRadius);
            TimeLeft = roundTime;
            judge.Reset();
        }

        public Vector3 Center => transform.position;
        public float BaseRadius => arenaRadius;
        public float CurrentRadius => Arena.CurrentRadius;
        public float RoundTime => roundTime;

        static Bot PickBot()
        {
            float total = 0f;
            foreach (var w in BotWeights) total += w;
            float r = Random.value * total;
            for (int k = 0; k < BotWeights.Length; k++)
            {
                r -= BotWeights[k];
                if (r <= 0f) return (Bot)k;
            }
            return Bot.Rush;
        }

        bool IsOut(FighterController f) => f.Body[Part.Hips].position.y - transform.position.y < fallHeight;

        void FixedUpdate()
        {
            if (Agent == null) return;
            TimeLeft -= Time.fixedDeltaTime;
            Arena.UpdateSuddenDeath(TimeLeft, suddenDeathTime, suddenDeathMinRadius);

            var outcome = judge.Update(IsOut(Fighters[0]), IsOut(Fighters[1]), Time.fixedDeltaTime);
            if (outcome == RingOutJudge.Outcome.None && (judge.Pending || TimeLeft > 0f)) return;

            int result; // +1 승, -1 패, 0 무
            float reward;
            bool mutual = outcome == RingOutJudge.Outcome.MutualFall;
            bool ringOutWin = outcome == RingOutJudge.Outcome.P1Win;
            if (outcome == RingOutJudge.Outcome.P1Win || outcome == RingOutJudge.Outcome.P2Win)
            {
                result = ringOutWin ? 1 : -1;
                reward = result * winReward;
            }
            else if (mutual) { result = 0; reward = mutualFallReward; }
            else { result = 0; reward = 0f; } // 시간 종료 = 무승부 (판정승 없음)

            Agent.AddReward(reward);
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Match/Win", result > 0 ? 1f : 0f);
            stats.Add($"Match/Win_vs_{CurrentBot}", result > 0 ? 1f : 0f);
            stats.Add($"Match/MutualFall_vs_{CurrentBot}", mutual ? 1f : 0f);
            stats.Add("Match/Loss", result < 0 ? 1f : 0f);
            stats.Add("Match/RingOutWin", ringOutWin ? 1f : 0f);
            stats.Add("Match/MutualFall", mutual ? 1f : 0f);
            stats.Add("Match/Length", roundTime - TimeLeft);
            episodes++;
            TrainingManager.ReportResult(result, ringOutWin, mutual, roundTime - TimeLeft);

            Agent.EndEpisode();
            ResetRound();
        }
    }
}
