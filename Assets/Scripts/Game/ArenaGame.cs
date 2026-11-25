using UnityEngine;

namespace RagdollFight
{
    /// <summary>
    /// 원형 경기장 + 파이터 2명 + 링아웃 판정 + 카메라 + 점수판을 코드로 만든다.
    /// 씬에 이 컴포넌트 하나만 있으면 바로 플레이할 수 있다.
    /// </summary>
    public class ArenaGame : MonoBehaviour, IFightContext
    {
        public enum Opponent { RushBot, GrabBot, RandomBot, FsmBot, AI, Keyboard2P, Idle, Watch_AI_vs_Rush, Watch_AI_vs_AI }

        [Header("Match")]
        public Opponent opponent = Opponent.RushBot;
        public float arenaRadius = 5f;
        public float roundTime = 60f;
        [Tooltip("남은 시간이 이보다 적으면 경기장이 줄어든다 (0이면 끔)")]
        public float suddenDeathTime = 20f;
        public float suddenDeathMinRadius = 1.0f;
        [Tooltip("엉덩이가 이 높이 아래로 떨어지면 링아웃")]
        public float fallHeight = -1.5f;
        public float spawnDistance = 2f;

        [Header("Rules")]
        [Tooltip("Physics = 원작처럼 물리만 (악력 무한, 팔 힘으로 들기·기어오르기). Stamina = 악력 게이지 규칙 (기존 모델 학습 규칙). 게임 중 설정 창에서 바꿀 수 있다.")]
        public RuleSet rules = RuleSet.Physics;

        [Header("Physics")]
        public float fixedTimestep = 0.01f;

        [Header("AI")]
        /// <summary>설정 창에서 고르는 AI 모델 (Assets/Resources/파일이름.onnx)</summary>
        static readonly (string file, string label)[] AiModels =
        {
            ("AI_PhysSP", "물리만 셀프플레이"),
            ("AI_PhysB", "물리만 보상 B"),
            ("AI_PhysNG", "물리만 (잡지 않는 러쉬봇)"),
            ("AI_SP5M", "셀프플레이 500만"),
            ("AI_SP2M", "셀프플레이 200만"),
            ("AI_B", "보상 B"),
            ("AI_A", "보상 A"),
            ("AI_mix2", "mix2"),
        };
        int aiIndex;      // 파랑(P2) AI 모델
        int p1AiIndex = 5; // AI끼리 관전할 때 빨강(P1) AI 모델
        bool settingsOpen;
        Vector2 settingsScroll;

        static readonly (Opponent value, string label)[] OpponentLabels =
        {
            (Opponent.RushBot, "러쉬 봇"), (Opponent.GrabBot, "잡기 봇"), (Opponent.RandomBot, "랜덤 봇"), (Opponent.FsmBot, "FSM 봇 (평가용)"),
            (Opponent.AI, "AI"), (Opponent.Keyboard2P, "2인 (키보드)"), (Opponent.Idle, "가만히"),
            (Opponent.Watch_AI_vs_Rush, "관전: 러쉬 봇 vs AI"), (Opponent.Watch_AI_vs_AI, "관전: AI vs AI"),
        };
        FighterAgent p1Agent;

        public FighterController[] Fighters { get; } = new FighterController[2];

        // IFightContext (AI가 관측하는 경기 정보)
        public Vector3 Center => Vector3.zero;
        public float BaseRadius => arenaRadius;
        public float CurrentRadius => arena.CurrentRadius;
        public float TimeLeft => roundTimer;
        public float RoundTime => roundTime;

        FighterAgent aiAgent;
        readonly RingOutJudge judge = new RingOutJudge();

        readonly int[] score = new int[2];
        float roundTimer;
        float endTimer = -1f;
        string message = "";
        FightCamera cam;
        Arena arena;

        static readonly Color[] FighterColors = { new Color(0.95f, 0.45f, 0.35f), new Color(0.35f, 0.6f, 0.95f) };

        void Awake()
        {
            Time.fixedDeltaTime = fixedTimestep;
            FighterController.Rules = rules;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;

            BuildArena();
            for (int i = 0; i < 2; i++)
            {
                SpawnPose(i, out var pos, out var rot);
                var ragdoll = Ragdoll.Create($"Fighter{i + 1}", pos, rot, FighterColors[i], transform);
                var fc = ragdoll.gameObject.AddComponent<FighterController>();
                fc.Init();
                Fighters[i] = fc;
            }
            SetupCamera();
            ApplyOpponent();
            StartRound();
        }

        void ApplyOpponent()
        {
            Fighters[0].InputSource = new KeyboardInputSource(KeyboardInputSource.Scheme.WasdMouse, cam);
            if (aiAgent != null) aiAgent.gameObject.SetActive(false);
            if (p1Agent != null) p1Agent.gameObject.SetActive(false);
            if (opponent == Opponent.Watch_AI_vs_AI)
            {
                // 관전: 빨강 AI vs 파랑 AI
                var m1 = Resources.Load<Unity.InferenceEngine.ModelAsset>(AiModels[p1AiIndex].file);
                if (m1 != null)
                {
                    if (p1Agent == null) p1Agent = FighterAgent.Create(transform, this, Fighters[0], Fighters[1], m1);
                    else
                    {
                        p1Agent.gameObject.SetActive(true);
                        p1Agent.SetModel("Fighter", m1);
                        Fighters[0].InputSource = p1Agent;
                    }
                }
                cam.mode = FightCamera.Mode.Overview;
            }
            if (opponent == Opponent.Watch_AI_vs_Rush)
            {
                // 관전: P1(빨강) = 러쉬봇, P2(파랑) = AI
                Fighters[0].InputSource = new RushBotInput(Fighters[0], Fighters[1]);
                cam.mode = FightCamera.Mode.Overview;
            }
            switch (opponent)
            {
                case Opponent.RushBot: Fighters[1].InputSource = new RushBotInput(Fighters[1], Fighters[0]); break;
                case Opponent.GrabBot: Fighters[1].InputSource = new GrabBotInput(Fighters[1], Fighters[0], this); break;
                case Opponent.RandomBot: Fighters[1].InputSource = new RandomBotInput(Fighters[1]); break;
                case Opponent.FsmBot: Fighters[1].InputSource = new FsmBotInput(Fighters[1], Fighters[0], this); break;
                case Opponent.AI:
                case Opponent.Watch_AI_vs_Rush:
                case Opponent.Watch_AI_vs_AI:
                    var aiModel = LoadAiModel();
                    if (aiModel == null)
                    {
                        Debug.LogWarning($"AI 모델이 없습니다 (Assets/Resources/{AiModels[aiIndex].file}.onnx). RushBot으로 대신합니다.");
                        Fighters[1].InputSource = new RushBotInput(Fighters[1], Fighters[0]);
                        break;
                    }
                    if (aiAgent == null) aiAgent = FighterAgent.Create(transform, this, Fighters[1], Fighters[0], aiModel);
                    else
                    {
                        aiAgent.gameObject.SetActive(true);
                        aiAgent.SetModel("Fighter", aiModel);
                        Fighters[1].InputSource = aiAgent;
                    }
                    break;
                case Opponent.Keyboard2P: Fighters[1].InputSource = new KeyboardInputSource(KeyboardInputSource.Scheme.Arrows, cam); break;
                default: Fighters[1].InputSource = new IdleInput(); break;
            }
        }

        Unity.InferenceEngine.ModelAsset LoadAiModel() =>
            Resources.Load<Unity.InferenceEngine.ModelAsset>(AiModels[aiIndex].file);

        bool UsesAi => opponent == Opponent.AI || opponent == Opponent.Watch_AI_vs_Rush || opponent == Opponent.Watch_AI_vs_AI;

        void SpawnPose(int index, out Vector3 pos, out Quaternion rot)
        {
            float side = index == 0 ? -1f : 1f;
            pos = new Vector3(side * spawnDistance, 0.05f, 0f);
            rot = Quaternion.LookRotation(new Vector3(-side, 0f, 0f), Vector3.up);
        }

        void BuildArena()
        {
            arena = Arena.Create(transform, arenaRadius);
            if (FindAnyObjectByType<Light>() == null)
            {
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                light.shadows = LightShadows.Soft;
            }
        }

        void SetupCamera()
        {
            var c = Camera.main;
            if (c == null)
            {
                c = new GameObject("Main Camera").AddComponent<Camera>();
                c.tag = "MainCamera";
            }
            c.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            c.clearFlags = CameraClearFlags.SolidColor;
            cam = c.GetComponent<FightCamera>();
            if (cam == null) cam = c.gameObject.AddComponent<FightCamera>();
            cam.target = Fighters[0].Body[Part.Hips];
            cam.overviewFocus = () =>
            {
                Vector3 mid = (Fighters[0].Body[Part.Hips].position + Fighters[1].Body[Part.Hips].position) * 0.5f;
                mid.y = 0f;
                return Vector3.ClampMagnitude(mid, arenaRadius) * 0.5f;
            };
        }

        public void StartRound()
        {
            for (int i = 0; i < 2; i++)
            {
                SpawnPose(i, out var pos, out var rot);
                Fighters[i].ResetFighter(pos, rot);
            }
            cam.SnapBehind(Fighters[0].Facing);
            roundTimer = roundTime;
            arena.SetRadius(arenaRadius);
            endTimer = -1f;
            message = "";
            judge.Reset();
            if (aiAgent != null && aiAgent.isActiveAndEnabled) aiAgent.EndEpisode();
            if (p1Agent != null && p1Agent.isActiveAndEnabled) p1Agent.EndEpisode();
        }

        void Update()
        {
            // Esc = 설정 창 열기/닫기 (열려 있는 동안 게임 일시정지)
            if (Input.GetKeyDown(KeyCode.Escape)) SetSettingsOpen(!settingsOpen);
            if (settingsOpen) return;
            if (Input.GetKeyDown(KeyCode.R)) StartRound();

            if (endTimer >= 0f)
            {
                endTimer -= Time.deltaTime;
                if (endTimer < 0f) StartRound();
                return;
            }

            roundTimer -= Time.deltaTime;
            arena.UpdateSuddenDeath(roundTimer, suddenDeathTime, suddenDeathMinRadius);
            // 한 명이 떨어져도 1초 더 지켜보고, 그 사이 둘 다 떨어지면 동시 낙하(무승부)
            var outcome = judge.Update(IsOut(Fighters[0]), IsOut(Fighters[1]), Time.deltaTime);
            if (outcome != RingOutJudge.Outcome.None || (roundTimer <= 0f && !judge.Pending))
            {
                if (outcome == RingOutJudge.Outcome.P2Win) { score[1]++; message = "P2 승리!"; }
                else if (outcome == RingOutJudge.Outcome.P1Win) { score[0]++; message = "P1 승리!"; }
                else if (outcome == RingOutJudge.Outcome.MutualFall) message = "동시 낙하 - 무승부";
                else message = "시간 종료 - 무승부";
                endTimer = 2f;
            }
        }

        void SetSettingsOpen(bool open)
        {
            settingsOpen = open;
            FightCamera.MenuOpen = open;
            Time.timeScale = open ? 0f : 1f;
        }

        void OnDisable()
        {
            FightCamera.MenuOpen = false;
            Time.timeScale = 1f;
        }

        /// <summary>설정 창: 규칙 모드, 상대, AI 모델. 바꾸면 바로 적용하고 라운드를 다시 시작한다.</summary>
        void DrawSettings()
        {
            float w = Mathf.Min(560f, Screen.width - 40f), h = Mathf.Min(600f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none); // 두 번 그려서 배경을 진하게
            var title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            var head = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 14 };

            GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, area.width - 32, area.height - 24));
            GUILayout.Label("설정", title);
            settingsScroll = GUILayout.BeginScrollView(settingsScroll);

            GUILayout.Label("게임 규칙", head);
            int r = GUILayout.Toolbar((int)FighterController.Rules, new[] { "물리만 (원작 방식)", "악력 게이지" }, btn, GUILayout.Height(30));
            GUILayout.Label(FighterController.Rules == RuleSet.Physics
                ? "악력 무한. 들기·기어오르기는 팔 힘으로 (매달려서 마우스 아래 = 턱걸이). 세게 잡아채이면 손이 빠짐."
                : "잡고 있으면 악력이 닳아 손이 풀림. 들기 보조, Space로 기어오르기. 기존 AI 모델들은 이 규칙으로 학습됨.", note);
            if (r != (int)FighterController.Rules) { FighterController.Rules = (RuleSet)r; StartRound(); }

            GUILayout.Space(10);
            GUILayout.Label("상대 (파랑)", head);
            int cur = System.Array.FindIndex(OpponentLabels, o => o.value == opponent);
            int sel = GUILayout.SelectionGrid(cur, System.Array.ConvertAll(OpponentLabels, o => o.label), 3, btn, GUILayout.Height(96));
            if (sel != cur) { opponent = OpponentLabels[sel].value; ApplyOpponent(); StartRound(); }

            var labels = System.Array.ConvertAll(AiModels, m => m.label);
            GUI.enabled = UsesAi;
            GUILayout.Space(10);
            GUILayout.Label("파랑 AI 모델", head);
            int a = GUILayout.SelectionGrid(aiIndex, labels, 3, btn, GUILayout.Height(64));
            if (a != aiIndex) { aiIndex = a; ApplyOpponent(); StartRound(); }

            GUI.enabled = opponent == Opponent.Watch_AI_vs_AI;
            GUILayout.Space(10);
            GUILayout.Label("빨강 AI 모델 (AI vs AI 관전)", head);
            int b = GUILayout.SelectionGrid(p1AiIndex, labels, 3, btn, GUILayout.Height(64));
            if (b != p1AiIndex) { p1AiIndex = b; ApplyOpponent(); StartRound(); }
            GUI.enabled = true;

            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (GUILayout.Button("닫기 (Esc)", btn, GUILayout.Height(32))) SetSettingsOpen(false);
            GUILayout.EndArea();
        }

        /// <summary>링아웃 판정: 엉덩이가 경기장 아래로 떨어졌는지.</summary>
        public bool IsOut(FighterController f) => f.Body[Part.Hips].position.y < fallHeight;

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(20, 15, 600, 40), $"P1 {score[0]} : {score[1]} P2    남은 시간 {Mathf.CeilToInt(Mathf.Max(roundTimer, 0))}", style);
            var small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            string oppLabel = System.Array.Find(OpponentLabels, o => o.value == opponent).label;
            if (UsesAi && opponent != Opponent.Watch_AI_vs_AI) oppLabel += $" ({AiModels[aiIndex].label})";
            string rulesLabel = FighterController.Rules == RuleSet.Physics ? "물리만" : "악력 게이지";
            GUI.Label(new Rect(20, 50, 1200, 25), $"P1: WASD 이동 · 마우스 = 몸 방향/팔 높이 · Space 점프 · 좌/우 클릭 = 왼손/오른손 잡기    |    상대: {oppLabel} · 규칙: {rulesLabel} · 시점 V · 리셋 R · 설정 Esc", small);
            if (opponent == Opponent.Watch_AI_vs_AI)
                GUI.Label(new Rect(20, 72, 900, 25), $"AI 대전 관전: 빨강 = {AiModels[p1AiIndex].label}   vs   파랑 = {AiModels[aiIndex].label}", small);
            if (opponent == Opponent.Keyboard2P)
                GUI.Label(new Rect(20, 72, 900, 25), "P2: 방향키 이동 · 오른쪽 Shift 점프 · , / . = 왼손/오른손 잡기", small);
            for (int i = 0; i < 2; i++)
            {
                var f = Fighters[i];
                float y = 100 + i * 26;
                GUI.Label(new Rect(20, y, 40, 22), $"P{i + 1}", small);
                if (!FighterController.PhysicsOnly)
                {
                    GUI.Box(new Rect(55, y + 3, 200, 14), GUIContent.none);
                    var prev = GUI.color;
                    GUI.color = f.IsExhausted ? Color.red : Color.Lerp(new Color(1f, 0.5f, 0.2f), new Color(0.3f, 1f, 0.4f), f.Stamina);
                    GUI.DrawTexture(new Rect(56, y + 4, 198 * f.Stamina, 12), Texture2D.whiteTexture);
                    GUI.color = prev;
                }
                string hang = FighterController.PhysicsOnly ? "매달림 (잡은 채 마우스 아래 = 턱걸이, 앞으로 가며 놓기)" : "매달림 (Space = 기어오르기)";
                string state = f.IsKnockedOut ? "기절!" : f.IsLifted ? "들림!" : f.IsHanging ? hang : f.IsExhausted ? "악력 소진" : "";
                GUI.Label(new Rect(265, y, 200, 22), state, small);
            }
            if (roundTimer < suddenDeathTime && endTimer < 0f)
                GUI.Label(new Rect(20, 155, 400, 25), "서든데스! 경기장이 줄어듭니다", small);

            // P1 팔 상태 표시 (팔 높이는 클릭하고 있을 때만 적용된다)
            var in0 = Fighters[0].LastInput;
            string hands = (in0.grabLeft ? "왼손 " : "") + (in0.grabRight ? "오른손" : "");
            if (hands.Length == 0) hands = "클릭하면 팔을 뻗음";
            GUI.Label(new Rect(20, Screen.height - 60, 400, 22), $"팔 높이 {in0.armPitch:+0.00;-0.00} (마우스 위/아래)  ·  {hands}", small);
            GUI.Box(new Rect(20, Screen.height - 34, 200, 12), GUIContent.none);
            GUI.DrawTexture(new Rect(20 + (in0.armPitch + 1f) * 0.5f * 196f, Screen.height - 34, 4, 12), Texture2D.whiteTexture);
            if (Cursor.lockState != CursorLockMode.Locked && cam.mode == FightCamera.Mode.ThirdPerson)
                GUI.Label(new Rect(Screen.width * 0.5f - 150, Screen.height * 0.5f + 40, 300, 25), "게임 화면을 클릭하면 마우스 시점이 켜집니다", small);
            if (settingsOpen) DrawSettings();
            else if (Cursor.lockState != CursorLockMode.Locked && GUI.Button(new Rect(Screen.width - 110, 15, 90, 30), "설정 (Esc)")) SetSettingsOpen(true);
            if (message.Length > 0)
            {
                var big = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 80), message, big);
            }
        }
    }
}
