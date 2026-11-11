using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFight.EditorTools
{
    /// <summary>
    /// 팔 힘(armReachStrength)을 바꿔가며 잡기·들어올리기·끌기 성능과 떨림을 잰다.
    /// </summary>
    public static class ArmStrengthSweep
    {
        const float Dt = 0.01f;

        [MenuItem("RagdollFight/Run Arm Strength Sweep")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var sb = new StringBuilder("[ArmSweep] strength | free arm tip(up=+1) | held arm tip(level) | lift: opp hip rise, opp airborne% | drag m | arm jitter rad/s\n");
            try
            {
                foreach (float strength in new[] { 1.2f, 2.5f, 5f, 10f })
                    sb.AppendLine(Measure(strength));
            }
            finally { Physics.simulationMode = prevMode; }
            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        class Scripted : IFighterInputSource
        {
            public FighterInput value;
            public FighterInput ReadInput() => value;
        }

        static string Measure(float strength)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            FighterController Make(string n, Vector3 p, Quaternion r)
            {
                var rd = Ragdoll.Create(n, p, r, Color.white);
                var f = rd.gameObject.AddComponent<FighterController>();
                f.Init();
                f.autoStep = false;
                f.armReachStrength = strength;
                f.gripDrainPerHand = 0f; // 측정 중에는 악력 소진 끔
                return f;
            }
            var me = Make("Me", new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            var opp = Make("Opp", new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
            var myIn = new Scripted(); me.InputSource = myIn;
            opp.InputSource = new Scripted();
            var chest = me.Body[Part.Chest];
            var arm = me.Body[Part.LowerArmR];

            void Run(int n) { for (int i = 0; i < n; i++) { me.Step(Dt); opp.Step(Dt); Physics.Simulate(Dt); } }
            float TipVsShoulder()
            {
                Vector3 shoulder = chest.position + chest.rotation * new Vector3(0.37f, 0.21f, 0f);
                return me.RightHand.TipPosition.y - shoulder.y;
            }

            // 1) 상대 없이 팔 위로 (기준값) — 상대를 옆으로 치워둔다
            opp.ResetFighter(new Vector3(5f, 0.05f, 0f), Quaternion.identity);
            Run(50);
            myIn.value = new FighterInput { grabRight = true, armPitch = 1f };
            Run(100);
            float freeUp = TipVsShoulder();

            // 2) 상대를 양손으로 잡고 수평 유지 → 팔이 처지는 정도
            me.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            opp.ResetFighter(new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
            myIn.value = default;
            Run(50);
            myIn.value = new FighterInput { grabLeft = true, grabRight = true, armPitch = 0f };
            Run(100);
            bool holding = me.LeftHand.GrabbedFighter == opp || me.RightHand.GrabbedFighter == opp;
            float heldLevel = TipVsShoulder();

            // 3) 들어올리기: 잡은 채로 팔을 위로
            float oppHip0 = opp.Body[Part.Hips].position.y;
            myIn.value = new FighterInput { grabLeft = true, grabRight = true, armPitch = 1f };
            float maxRise = 0f, jitter = 0f;
            int airborne = 0, n = 150;
            for (int i = 0; i < n; i++)
            {
                me.Step(Dt); opp.Step(Dt); Physics.Simulate(Dt);
                maxRise = Mathf.Max(maxRise, opp.Body[Part.Hips].position.y - oppHip0);
                if (!opp.IsGrounded) airborne++;
                jitter += arm.angularVelocity.magnitude;
            }
            jitter /= n;

            // 4) 끌기: 수평으로 잡고 2초 뒤로 걷기
            me.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            opp.ResetFighter(new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
            myIn.value = default;
            Run(50);
            myIn.value = new FighterInput { grabLeft = true, grabRight = true };
            Run(80);
            float z0 = opp.Body[Part.Hips].position.z;
            myIn.value = new FighterInput { grabLeft = true, grabRight = true, move = new Vector2(0f, -1f), aim = new Vector2(0f, 1f) };
            Run(200);
            float drag = z0 - opp.Body[Part.Hips].position.z;

            return $"  x{strength,-4} | {freeUp,6:F2} | {heldLevel,6:F2} (held={holding}) | {maxRise,5:F2}m, {100f * airborne / n,3:F0}% | {drag,5:F2} | {jitter,5:F2}";
        }
    }
}
