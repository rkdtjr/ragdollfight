using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFight.EditorTools
{
    /// <summary>
    /// 화면 없이 P1(스크립트 전략) vs P2(Rush Bot) 대전을 여러 판 돌려 승률을 본다.
    /// "사람이 봇을 이길 수 있는 게임인가"를 숫자로 확인하는 용도.
    /// </summary>
    public static class MatchSimulator
    {
        const float Dt = 0.01f;
        const float ArenaRadius = 5f;
        const float FallHeight = -1.5f;
        const float RoundTime = 60f;
        const float SuddenDeathTime = 20f;
        const float SuddenDeathMinRadius = 1.0f;
        const int Matches = 12;

        [MenuItem("RagdollFight/Run Match Simulation")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var report = new StringBuilder("[MatchSim] P1 strategy vs RushBot (W/L/D, avg sec)\n");
            try
            {
                foreach (var strategy in new[] { "Idle", "Mirror", "GrabPushOut", "EdgeDodge", "GrabSideDrag", "GrabLiftCarry" })
                {
                    int w = 0, l = 0, d = 0;
                    float totalTime = 0f;
                    for (int m = 0; m < Matches; m++)
                    {
                        int result = PlayMatch(strategy, m, out float time);
                        if (result > 0) w++; else if (result < 0) l++; else d++;
                        totalTime += time;
                    }
                    report.AppendLine($"  {strategy,-13} W {w,2}  L {l,2}  D {d,2}   avg {totalTime / Matches:F1}s");
                }
            }
            finally { Physics.simulationMode = prevMode; }
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <returns>+1 P1 승, -1 P1 패, 0 무승부</returns>
        static int PlayMatch(string strategy, int seed, out float time)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rng = new System.Random(seed * 7919 + strategy.GetHashCode());
            float Jitter(float a) => (float)(rng.NextDouble() * 2 - 1) * a;

            var arena = Arena.Create(null, ArenaRadius);

            var f = new FighterController[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var pos = new Vector3(side * 2f + Jitter(0.5f), 0.05f, Jitter(1.5f));
                var rot = Quaternion.LookRotation(new Vector3(-side, 0f, Jitter(0.3f)), Vector3.up);
                var r = Ragdoll.Create("F" + i, pos, rot, Color.white);
                f[i] = r.gameObject.AddComponent<FighterController>();
                f[i].Init();
                f[i].autoStep = false;
            }
            f[0].InputSource = MakeStrategy(strategy, f[0], f[1]);
            f[1].InputSource = new RushBotInput(f[1], f[0]);

            int steps = Mathf.RoundToInt(RoundTime / Dt);
            for (int s = 0; s < steps; s++)
            {
                f[0].Step(Dt); f[1].Step(Dt);
                arena.UpdateSuddenDeath(RoundTime - s * Dt, SuddenDeathTime, SuddenDeathMinRadius);
                Physics.Simulate(Dt);
                bool out0 = f[0].Body[Part.Hips].position.y < FallHeight;
                bool out1 = f[1].Body[Part.Hips].position.y < FallHeight;
                if (out0 || out1)
                {
                    time = s * Dt;
                    return out0 && out1 ? 0 : (out1 ? 1 : -1);
                }
            }
            time = RoundTime;
            return Arena.JudgeTimeout(f[0].Body[Part.Hips].position, f[1].Body[Part.Hips].position);
        }

        static IFighterInputSource MakeStrategy(string name, FighterController self, FighterController opp)
        {
            switch (name)
            {
                case "Mirror": return new RushBotInput(self, opp);
                case "GrabPushOut": return new ScriptedStrategy(self, opp, ScriptedStrategy.Kind.GrabPushOut);
                case "EdgeDodge": return new ScriptedStrategy(self, opp, ScriptedStrategy.Kind.EdgeDodge);
                case "GrabSideDrag": return new ScriptedStrategy(self, opp, ScriptedStrategy.Kind.GrabSideDrag);
                case "GrabLiftCarry": return new ScriptedStrategy(self, opp, ScriptedStrategy.Kind.GrabLiftCarry);
                default: return new IdleInput();
            }
        }

        /// <summary>사람이 쓸 법한 단순 전략들.</summary>
        class ScriptedStrategy : IFighterInputSource
        {
            public enum Kind { GrabPushOut, EdgeDodge, GrabSideDrag, GrabLiftCarry }
            readonly FighterController self, opp;
            readonly Kind kind;
            float dodgeSign = 1f;
            float throwTimer;

            public ScriptedStrategy(FighterController self, FighterController opp, Kind kind)
            {
                this.self = self; this.opp = opp; this.kind = kind;
            }

            static Vector2 V(Vector3 v) => new Vector2(v.x, v.z);
            static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

            public FighterInput ReadInput()
            {
                var i = new FighterInput();
                Vector3 me = Flat(self.Body[Part.Chest].position);
                Vector3 them = Flat(opp.Body[Part.Chest].position);
                Vector3 toThem = them - me;
                float dist = toThem.magnitude;
                bool holding = self.LeftHand.GrabbedFighter == opp || self.RightHand.GrabbedFighter == opp;

                i.aim = V(toThem); // 항상 상대를 바라본다 (마우스로 상대를 겨누는 사람처럼)
                switch (kind)
                {
                    case Kind.GrabPushOut:
                        // 다가가서 잡고, 상대를 경기장 바깥 방향으로 민다.
                        i.grabLeft = i.grabRight = dist < 1.4f;
                        i.move = holding ? V(them.normalized) : V(toThem);
                        break;

                    case Kind.GrabSideDrag:
                        // 잡은 뒤 옆으로 돌면서 상대를 가장 가까운 가장자리 쪽으로 끈다.
                        i.grabLeft = i.grabRight = dist < 1.4f;
                        if (holding)
                        {
                            Vector3 side = Vector3.Cross(Vector3.up, toThem.normalized);
                            i.move = V(side + them.normalized * 0.5f);
                        }
                        else i.move = V(toThem);
                        break;

                    case Kind.GrabLiftCarry:
                        // 잡고 팔을 들어 상대를 띄운 뒤, 상대 쪽 가장자리로 들고 간다.
                        bool nearEdge = them.magnitude > ArenaRadius - 1.3f;
                        if (nearEdge && (holding || throwTimer > 0f))
                        {
                            // 가장자리 근처: 던지듯 놓고 가운데로 물러난다
                            throwTimer = throwTimer > 0f ? throwTimer - 0.01f : 0.6f;
                            i.move = V(-me.normalized);
                            break;
                        }
                        throwTimer = 0f;
                        i.grabLeft = i.grabRight = dist < 1.4f;
                        if (holding)
                        {
                            i.armPitch = 1f;
                            // 상대를 앞에 둔 채 밀고 가되, 상대 쪽 가장자리로 방향을 튼다
                            Vector3 outward = them.sqrMagnitude > 0.01f ? them.normalized : Vector3.zero;
                            i.move = V(toThem.normalized + outward);
                        }
                        else i.move = V(toThem);
                        break;

                    case Kind.EdgeDodge:
                        // 가장자리 근처에서 기다리다가 상대가 달려오면 옆으로 피한다 (투우사 전략).
                        float r = me.magnitude;
                        if (dist < 1.8f)
                        {
                            Vector3 side = Vector3.Cross(Vector3.up, toThem.normalized) * dodgeSign;
                            i.move = V(side - me.normalized * 0.4f);
                        }
                        else if (r < 3.5f)
                        {
                            i.move = V(me.sqrMagnitude > 0.01f ? me.normalized : Vector3.left);
                            dodgeSign = -dodgeSign;
                        }
                        break;
                }
                return i;
            }
        }
    }
}
