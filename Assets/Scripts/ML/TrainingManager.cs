using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 학습 씬: 경기장 여러 개를 격자로 띄운다.
    /// 실행 인자:
    ///   -arenas N     경기장 개수
    ///   -eval N       학습 대신 평가: Resources/FighterAI.onnx 모델로 N판을 두고 결과를 로그에 남긴 뒤 종료
    ///   -timescale X  평가 때 게임 속도 배율
    ///   -bot Rush|Grab|Random  상대 봇 고정 (기본: 섞어서)
    ///   -shaping 0|1  유도 보상(접근·잡기·밀어내기) 끄기/켜기. 0 = 보상 A, 1 = 보상 B (기본)
    ///   -mutualFall X 동시 낙하 보상 (기본 -0.5)
    ///   -model NAME   평가할 모델 (Resources/NAME.onnx, 기본 AI_B)
    ///   -opponentModel NAME  상대도 AI로 (AI vs AI 평가)
    ///   -selfplay R   경기장 중 비율 R을 셀프 플레이(AI vs AI 학습)로, 나머지는 봇 상대 (기본 0)
    /// </summary>
    public class TrainingManager : MonoBehaviour
    {
        public int arenaCount = 8;
        public float spacing = 25f;
        public float fixedTimestep = 0.01f;

        // 보상 설정 (실행 인자로 바꿀 수 있다)
        public static bool Shaping = true;
        public static float MutualFallReward = -0.5f;
        public static float SelfPlayRatio = 0f;

        static string modelName = "AI_B";
        static string opponentModelName;

        // 평가 모드 집계
        static int evalGames, played, wins, losses, ringOutWins, mutualFalls;
        static float totalLength;

        public static void ReportResult(int result, bool ringOut, bool mutual, float length)
        {
            if (evalGames <= 0 || played >= evalGames) return;
            played++;
            if (result > 0) wins++;
            if (result < 0) losses++;
            if (result > 0 && ringOut) ringOutWins++;
            if (mutual) mutualFalls++;
            totalLength += length;
            if (played % 50 == 0 || played == evalGames)
                Debug.Log($"[Eval] {played}/{evalGames}  win {wins} ({100f * wins / played:F1}%)  loss {losses}  draw {played - wins - losses} (mutualFall {mutualFalls})  ringOutWin {ringOutWins}  avgLen {totalLength / played:F1}s");
            if (played == evalGames) Application.Quit(0);
        }

        void Awake()
        {
            Time.fixedDeltaTime = fixedTimestep;
            Time.maximumDeltaTime = 0.1f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-arenas" && int.TryParse(args[i + 1], out int n)) arenaCount = n;
                if (args[i] == "-eval" && int.TryParse(args[i + 1], out int e)) evalGames = e;
                if (args[i] == "-timescale" && float.TryParse(args[i + 1], out float ts)) Time.timeScale = ts;
                if (args[i] == "-shaping") Shaping = args[i + 1] != "0";
                if (args[i] == "-mutualFall" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mf)) MutualFallReward = mf;
                if (args[i] == "-model") modelName = args[i + 1];
                if (args[i] == "-selfplay" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sp)) SelfPlayRatio = sp;
                if (args[i] == "-opponentModel") opponentModelName = args[i + 1];
                if (args[i] == "-bot" && System.Enum.TryParse(args[i + 1], true, out TrainingArena.Bot b)) TrainingArena.ForcedBot = b;
            }

            Debug.Log($"[Reward] shaping={(Shaping ? "B" : "A")} mutualFall={MutualFallReward} selfplay={SelfPlayRatio}");
            int selfPlayArenas = evalGames > 0 ? 0 : Mathf.RoundToInt(arenaCount * SelfPlayRatio);
            Unity.InferenceEngine.ModelAsset model = null;
            if (evalGames > 0)
            {
                model = Resources.Load<Unity.InferenceEngine.ModelAsset>(modelName);
                if (opponentModelName != null)
                    TrainingArena.OpponentModel = Resources.Load<Unity.InferenceEngine.ModelAsset>(opponentModelName);
                string vs = opponentModelName != null ? $"AI {opponentModelName}" : (TrainingArena.ForcedBot?.ToString() ?? "mix");
                Debug.Log($"[Eval] {evalGames} games, {modelName} vs {vs}");
                if (model == null || (opponentModelName != null && TrainingArena.OpponentModel == null))
                {
                    Debug.LogError("[Eval] model not found in Resources");
                    Application.Quit(1);
                    return;
                }
            }

            int cols = Mathf.CeilToInt(Mathf.Sqrt(arenaCount));
            for (int k = 0; k < arenaCount; k++)
            {
                var go = new GameObject($"TrainingArena{k}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3((k % cols) * spacing, 0f, (k / cols) * spacing);
                var ta = go.AddComponent<TrainingArena>();
                ta.selfPlay = k < selfPlayArenas;
                ta.Build(model);
            }

            // 화면이 있을 때: 모든 경기장이 보이도록 카메라를 맞춘다
            var cam = Camera.main;
            if (cam != null)
            {
                int rows = Mathf.CeilToInt(arenaCount / (float)cols);
                var center = new Vector3((cols - 1) * spacing * 0.5f, 0f, (rows - 1) * spacing * 0.5f);
                float h = Mathf.Max(cols, rows) * spacing * 0.75f + 6f;
                cam.transform.position = center + new Vector3(0f, h, -h * 0.75f);
                cam.transform.LookAt(center);
                cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
                cam.clearFlags = CameraClearFlags.SolidColor;
            }
        }
    }
}
