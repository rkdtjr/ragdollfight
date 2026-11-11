using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 강화학습 에이전트. 사람·봇과 똑같이 FighterInput만 만들어서 파이터를 조종한다.
    ///
    /// 기준 좌표계: "상대를 바라보는 방향"이 앞(+z). 관측과 이동 행동 모두 이 좌표계를 쓴다.
    /// 그래서 경기장 어느 쪽에서 시작하든 같은 정책을 쓸 수 있다.
    ///
    /// 행동 (MultiDiscrete):
    ///   0 이동 9방향 (0 = 정지, 1~8 = 앞부터 시계방향)
    ///   1 왼손 잡기 2, 2 오른손 잡기 2, 3 점프 2
    ///   4 몸 방향 2 (0 = 이동 방향, 1 = 상대 쪽)
    ///   5 팔 높이 3 (아래 / 수평 / 위)
    /// </summary>
    public class FighterAgent : Agent, IFighterInputSource
    {
        public const int ObservationSize = 40;
        public static readonly int[] ActionBranches = { 9, 2, 2, 2, 2, 3 };

        [Header("Shaping rewards (보상 B)")]
        [Tooltip("초당 시간 벌점. 0.01 = 판단(0.05초)당 0.0005, 30초 경기 끝까지 가면 -0.3")]
        public float timePenaltyPerSecond = 0.01f;
        public float approachReward = 0.01f;
        public float grabReward = 0.05f;
        public float pushOutReward = 0.02f;
        [Tooltip("가장자리 위험 영역에 들어갔다가 안쪽 안전 영역으로 돌아오면 한 판에 한 번 주는 보상")]
        public float safeReturnReward = 0.05f;
        [Tooltip("가장자리까지 거리가 현재 반지름의 이 비율보다 작으면 위험 영역")]
        public float dangerZone = 0.2f;
        [Tooltip("가장자리까지 거리가 현재 반지름의 이 비율보다 크면 안전 영역")]
        public float safeZone = 0.4f;

        public IFightContext Env { get; set; }
        public FighterController Self { get; set; }
        public FighterController Opponent { get; set; }

        FighterInput current;
        Quaternion frame = Quaternion.identity;   // 월드 → 기준 좌표계
        float prevDistance;
        float prevOppEdge;
        bool grabbedThisEpisode;
        bool wasInDanger, safeReturnGiven;

        public FighterInput ReadInput() => current;

        /// <summary>
        /// 에이전트를 만들고 self 파이터에 연결한다.
        /// model이 null이면 학습용(파이썬 트레이너가 조종), 있으면 그 모델로 추론만 한다.
        /// </summary>
        public static FighterAgent Create(Transform parent, IFightContext env, FighterController self, FighterController opponent,
                                          Unity.InferenceEngine.ModelAsset model)
        {
            // BehaviorParameters를 먼저 설정한 뒤 켜야 한다.
            var go = new GameObject($"Agent ({self.name})");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var bp = go.AddComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            bp.BehaviorName = "Fighter";
            bp.BrainParameters.VectorObservationSize = ObservationSize;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(ActionBranches);
            if (model != null)
            {
                bp.Model = model;
                bp.BehaviorType = Unity.MLAgents.Policies.BehaviorType.InferenceOnly;
            }
            var agent = go.AddComponent<FighterAgent>();
            agent.Env = env;
            agent.Self = self;
            agent.Opponent = opponent;
            var dr = go.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 5; // 0.05초마다 판단
            dr.TakeActionsBetweenDecisions = true;
            self.InputSource = agent;
            go.SetActive(true);
            return agent;
        }

        public override void OnEpisodeBegin()
        {
            current = new FighterInput();
            UpdateFrame();
            prevDistance = Distance();
            prevOppEdge = Env.EdgeDistance(Opponent);
            grabbedThisEpisode = false;
            wasInDanger = safeReturnGiven = false;
        }

        void UpdateFrame()
        {
            Vector3 d = Opponent.Body[Part.Hips].position - Self.Body[Part.Hips].position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) frame = Quaternion.Inverse(Quaternion.LookRotation(d.normalized, Vector3.up));
        }

        float Distance()
        {
            Vector3 d = Opponent.Body[Part.Hips].position - Self.Body[Part.Hips].position;
            d.y = 0f;
            return d.magnitude;
        }

        public override void CollectObservations(VectorSensor s)
        {
            UpdateFrame();
            float r0 = Env.BaseRadius;
            Vector3 c = Env.Center;

            // --- 나 (19) ---
            var hips = Self.Body[Part.Hips];
            Vector3 myPos = frame * (hips.position - c);
            s.AddObservation(myPos.x / r0);
            s.AddObservation(myPos.z / r0);
            s.AddObservation(Env.EdgeDistance(Self) / r0);
            s.AddObservation(hips.position.y - c.y);
            s.AddObservation(frame * hips.linearVelocity / 5f);              // 3
            s.AddObservation(frame * Self.Body[Part.Chest].transform.up);     // 3
            s.AddObservation(Self.IsGrounded);
            s.AddObservation(Self.Consciousness);
            s.AddObservation(Self.Stamina);
            s.AddObservation(Self.IsLifted);
            s.AddObservation(Self.IsHanging);
            AddHand(s, Self.LeftHand);                                        // 2
            AddHand(s, Self.RightHand);                                       // 2

            // --- 상대 (19) ---
            var oh = Opponent.Body[Part.Hips];
            s.AddObservation(frame * (oh.position - hips.position) / 5f);     // 3
            Vector3 oppPos = frame * (oh.position - c);
            s.AddObservation(oppPos.x / r0);
            s.AddObservation(oppPos.z / r0);
            s.AddObservation(Env.EdgeDistance(Opponent) / r0);
            s.AddObservation(frame * oh.linearVelocity / 5f);                 // 3
            s.AddObservation(frame * Opponent.Body[Part.Chest].transform.up); // 3
            Vector3 of = frame * Opponent.Facing;
            s.AddObservation(of.x);
            s.AddObservation(of.z);
            s.AddObservation(Opponent.IsGrounded);
            s.AddObservation(Opponent.Consciousness);
            s.AddObservation(Opponent.Stamina);
            s.AddObservation(Opponent.IsLifted);
            s.AddObservation(Opponent.LeftHand.GrabbedFighter == Self || Opponent.RightHand.GrabbedFighter == Self);

            // --- 경기 (2) ---
            s.AddObservation(Env.CurrentRadius / r0);
            s.AddObservation(Env.TimeLeft / Env.RoundTime);
        }

        static void AddHand(VectorSensor s, HandGrabber h)
        {
            s.AddObservation(h.GrabbedFighter != null);
            s.AddObservation(h.IsGrabbingWorld);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var a = actions.DiscreteActions;
            Quaternion toWorld = Quaternion.Inverse(frame);

            var i = new FighterInput();
            Vector2 m = FighterInput.MoveFromIndex(a[0]); // 기준 좌표계 기준 (y = 앞)
            Vector3 mw = toWorld * new Vector3(m.x, 0f, m.y);
            i.move = new Vector2(mw.x, mw.z);
            i.grabLeft = a[1] == 1;
            i.grabRight = a[2] == 1;
            i.jump = a[3] == 1;
            if (a[4] == 1)
            {
                Vector3 fw = toWorld * Vector3.forward;
                i.aim = new Vector2(fw.x, fw.z);
            }
            i.armPitch = a[5] - 1f;
            current = i;

            // --- 보상 B (작은 shaping) ---
            // 주의: DecisionRequester.TakeActionsBetweenDecisions 때문에 이 함수는 판단할 때만이 아니라
            // 물리 스텝마다 불린다. 그래서 시간 벌점은 경과 시간(dt)에 비례하게 준다.
            // 거리 변화 보상은 직전 호출과의 차이라서 호출 횟수와 무관하다.
            AddReward(-timePenaltyPerSecond * Time.fixedDeltaTime);

            float dist = Distance();
            if (dist > 0.8f) AddReward(approachReward * Mathf.Clamp(prevDistance - dist, -0.5f, 0.5f));
            prevDistance = dist;

            bool holding = Self.LeftHand.GrabbedFighter == Opponent || Self.RightHand.GrabbedFighter == Opponent;
            if (holding && !grabbedThisEpisode)
            {
                grabbedThisEpisode = true;
                AddReward(grabReward);
            }

            float oppEdge = Env.EdgeDistance(Opponent);
            if (holding) AddReward(pushOutReward * Mathf.Clamp(prevOppEdge - oppEdge, -0.5f, 0.5f));
            prevOppEdge = oppEdge;

            // 안전 영역 복귀 (한 판에 한 번)
            float myEdge = Env.EdgeDistance(Self) / Mathf.Max(Env.CurrentRadius, 0.01f);
            if (myEdge < dangerZone) wasInDanger = true;
            else if (wasInDanger && !safeReturnGiven && myEdge > safeZone)
            {
                safeReturnGiven = true;
                AddReward(safeReturnReward);
            }
        }

        /// <summary>사람이 직접 조종해볼 때 (Behavior Type = Heuristic Only).</summary>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var a = actionsOut.DiscreteActions;
            for (int k = 0; k < a.Length; k++) a[k] = 0;
            a[5] = 1;
        }
    }
}
