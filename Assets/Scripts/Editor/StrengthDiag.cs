using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFight.EditorTools
{
    /// <summary>
    /// 힘·들어올리기 진단: 드는 쪽과 들린 쪽의 자세를 시간별로 기록한다.
    /// </summary>
    public static class StrengthDiag
    {
        class Scripted : IFighterInputSource
        {
            public FighterInput value;
            public FighterInput ReadInput() => value;
        }

        [MenuItem("RagdollFight/Run Strength Diagnostics")]
        public static void Run()
        {
            var prevMode = Physics.simulationMode;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.simulationMode = SimulationMode.Script;
            const float dt = 0.01f;
            try
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);

                FighterController Make(string n, Vector3 p, float yaw)
                {
                    var rd = Ragdoll.Create(n, p, Quaternion.Euler(0f, yaw, 0f), Color.white);
                    var fc = rd.gameObject.AddComponent<FighterController>();
                    fc.Init();
                    fc.autoStep = false;
                    return fc;
                }
                var a = Make("Lifter", new Vector3(0f, 0.05f, 0f), 0f);
                var b = Make("Victim", new Vector3(0f, 0.05f, 1.0f), 180f);
                var ia = new Scripted(); var ib = new Scripted();
                a.InputSource = ia; b.InputSource = ib;

                void Step(int n) { for (int s = 0; s < n; s++) { a.Step(dt); b.Step(dt); Physics.Simulate(dt); } }
                float Tilt(Rigidbody rb) => Vector3.Angle(rb.transform.up, Vector3.up);
                string State(FighterController f) =>
                    $"hipY={f.Body[Part.Hips].position.y:F2} chestTilt={Tilt(f.Body[Part.Chest]):F0}° hipTilt={Tilt(f.Body[Part.Hips]):F0}° grounded={(f.IsGrounded ? 1 : 0)}";

                foreach (bool struggle in new[] { false, true })
                {
                    a.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
                    b.ResetFighter(new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
                    ia.value = default; ib.value = default;
                    Step(60);
                    ia.value = new FighterInput { grabLeft = true, grabRight = true };
                    Step(80);
                    Debug.Log($"[Diag] --- lift (victim struggles={struggle}) --- holding={(a.LeftHand.IsGrabbing ? 1 : 0) + (a.RightHand.IsGrabbing ? 1 : 0)}");
                    ia.value = new FighterInput { grabLeft = true, grabRight = true, armPitch = 1f };
                    if (struggle) ib.value = new FighterInput { move = new Vector2(0f, 1f) };
                    for (int t = 0; t < 12; t++)
                    {
                        Step(25);
                        int hold = (a.LeftHand.IsGrabbing ? 1 : 0) + (a.RightHand.IsGrabbing ? 1 : 0);
                        Debug.Log($"[Diag] t={(t + 1) * 0.25f:F2}s hold={hold} stam={a.Stamina:F2} | LIFTER {State(a)} | VICTIM {State(b)} lifted={(b.IsLifted ? 1 : 0)}");
                    }
                }

                // 끌고 가기: 잡은 채로 뒤로 걷기
                a.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
                b.ResetFighter(new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
                ia.value = default; ib.value = default;
                Step(60);
                ia.value = new FighterInput { grabLeft = true, grabRight = true };
                Step(80);
                ia.value = new FighterInput { grabLeft = true, grabRight = true, move = new Vector2(0f, -1f), aim = new Vector2(0f, 1f) };
                ib.value = new FighterInput { move = new Vector2(0f, 1f) }; // 버티기 (끌려가는 반대쪽으로 걷기)
                Debug.Log("[Diag] --- drag backward vs resisting victim ---");
                for (int t = 0; t < 8; t++)
                {
                    Step(25);
                    Debug.Log($"[Diag] t={(t + 1) * 0.25f:F2}s lifterZ={a.Body[Part.Hips].position.z:F2} victimZ={b.Body[Part.Hips].position.z:F2} | LIFTER {State(a)} | VICTIM {State(b)}");
                }
            }
            finally
            {
                Physics.simulationMode = prevMode;
            }
        }
    }
}
