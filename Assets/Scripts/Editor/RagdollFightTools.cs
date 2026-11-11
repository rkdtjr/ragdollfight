using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFight.EditorTools
{
    public static class RagdollFightTools
    {
        const string ScenePath = "Assets/Scenes/Arena.unity";

        [MenuItem("RagdollFight/Create Arena Scene")]
        public static void CreateArenaScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("ArenaGame").AddComponent<ArenaGame>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[RagdollFight] Arena scene created: " + ScenePath);
        }

        /// <summary>
        /// 화면 없이 물리만 돌려서 래그돌이 제대로 서고·걷고·팔을 뻗고·점프하고·기절 후 일어나는지 확인한다.
        /// 커맨드라인: Unity.exe -batchmode -projectPath . -executeMethod RagdollFight.EditorTools.RagdollFightTools.RunPhysicsSelfTest -quit
        /// </summary>
        [MenuItem("RagdollFight/Run Physics Self-Test")]
        public static void RunPhysicsSelfTest()
        {
            bool ok = true;
            var prevMode = Physics.simulationMode;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                const float dt = 0.01f;

                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.transform.position = new Vector3(0f, -0.5f, 0f);

                var ragdoll = Ragdoll.Create("TestFighter", new Vector3(0f, 0.05f, 0f), Quaternion.identity, Color.red);
                var fc = ragdoll.gameObject.AddComponent<FighterController>();
                fc.Init();
                fc.autoStep = false;
                var input = new ScriptedInput();
                fc.InputSource = input;
                var hips = ragdoll[Part.Hips];
                var chest = ragdoll[Part.Chest];

                void Run(int steps)
                {
                    for (int s = 0; s < steps; s++) { fc.Step(dt); Physics.Simulate(dt); }
                }
                float Tilt(Rigidbody rb) => Vector3.Angle(rb.transform.up, Vector3.up);
                void Check(string name, bool pass, string detail)
                {
                    Debug.Log($"[SelfTest] {(pass ? "PASS" : "FAIL")} {name}: {detail}");
                    ok &= pass;
                }

                // 1) 가만히 서 있기
                Run(300);
                Check("stand", hips.position.y > 0.65f && Tilt(chest) < 15f,
                    $"hipY={hips.position.y:F2} chestTilt={Tilt(chest):F1}°");

                // 2) 앞으로 걷기 (+Z)
                float z0 = hips.position.z;
                input.value = new FighterInput { move = new Vector2(0f, 1f) };
                Run(200);
                float walked = hips.position.z - z0;
                Check("walk", walked > 2.5f && Tilt(chest) < 30f && hips.position.y > 0.55f,
                    $"dz={walked:F2}m in 2s, vz={hips.linearVelocity.z:F2} chestTilt={Tilt(chest):F1}° hipY={hips.position.y:F2}");

                // 3) 옆으로 돌기 (+X)
                input.value = new FighterInput { move = new Vector2(1f, 0f) };
                Run(150);
                float facingErr = Vector3.Angle(Flat(chest.transform.forward), Vector3.right);
                Check("turn", facingErr < 25f, $"chest facing error vs +X = {facingErr:F1}°");

                // 4) 오른손 뻗기 → 손이 가슴 앞쪽으로 나와야 한다
                input.value = new FighterInput { grabRight = true };
                Run(100);
                Vector3 handLocal = Quaternion.Inverse(chest.rotation) * (fc.RightHand.TipPosition - chest.position);
                Vector3 idleLocal = Quaternion.Inverse(chest.rotation) * (fc.LeftHand.TipPosition - chest.position);
                Check("reach", handLocal.z > 0.5f && idleLocal.z < 0.3f,
                    $"right hand fwd={handLocal.z:F2} up={handLocal.y:F2}, idle left hand fwd={idleLocal.z:F2}");

                // 4-1) 팔 높이 조절: armPitch -1 / 0 / +1 일 때 손끝 높이 (어깨 기준)
                var tipHeights = new float[3];
                for (int k = 0; k < 3; k++)
                {
                    input.value = default; // 매번 손을 놓고 다시 뻗는다 (아래로 뻗으면 바닥을 잡기 때문)
                    Run(60);
                    input.value = new FighterInput { grabRight = true, armPitch = k - 1 };
                    Run(100);
                    Vector3 shoulder = chest.position + chest.rotation * new Vector3(0.37f, 0.21f, 0f);
                    tipHeights[k] = fc.RightHand.TipPosition.y - shoulder.y;
                }
                Check("arm pitch", tipHeights[0] < -0.3f && Mathf.Abs(tipHeights[1]) < 0.25f && tipHeights[2] > 0.4f,
                    $"tip height vs shoulder: down={tipHeights[0]:F2} level={tipHeights[1]:F2} up={tipHeights[2]:F2}");

                // 4-2) 조준: +Z로 걸으면서 +X를 바라보기 (옆걸음)
                input.value = new FighterInput { move = new Vector2(0f, 1f), aim = new Vector2(1f, 0f) };
                Run(150);
                float aimErr = Vector3.Angle(Flat(chest.transform.forward), Vector3.right);
                Check("aim while strafing", aimErr < 20f && hips.linearVelocity.z > 1.5f,
                    $"facing error vs aim={aimErr:F1}°, vz={hips.linearVelocity.z:F2}");

                // 5) 점프
                input.value = new FighterInput { jump = true };
                float maxY = hips.position.y;
                for (int s = 0; s < 80; s++) { fc.Step(dt); Physics.Simulate(dt); maxY = Mathf.Max(maxY, hips.position.y); input.value = default; }
                Check("jump", maxY > 1.2f && maxY < 2.0f, $"max hipY={maxY:F2}");
                Run(200);

                // 6) 기절 → 쓰러졌다가 다시 일어나기
                input.value = default;
                fc.KnockOut(1.5f);
                Run(150);
                float koY = hips.position.y;
                Run(300);
                Check("knockout+recover", koY < 0.6f && hips.position.y > 0.65f && Tilt(chest) < 20f,
                    $"hipY during KO={koY:F2}, after recover={hips.position.y:F2} chestTilt={Tilt(chest):F1}°");

                // 7) 세게 밀어도 버티는지 (흐물거리지만 쓰러지지 않음)
                chest.AddForce(Vector3.right * 3f, ForceMode.VelocityChange);
                Run(300);
                Check("push recover", hips.position.y > 0.65f && Tilt(chest) < 20f,
                    $"hipY={hips.position.y:F2} chestTilt={Tilt(chest):F1}°");

                // 8) 상대 잡고 끌기: 앞에 가만히 있는 상대를 양손으로 잡고 뒤로 걸어간다
                fc.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
                var other = Ragdoll.Create("Dummy", new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f), Color.blue);
                var ofc = other.gameObject.AddComponent<FighterController>();
                ofc.Init();
                ofc.autoStep = false;
                ofc.InputSource = new ScriptedInput();
                void RunBoth(int steps)
                {
                    for (int s = 0; s < steps; s++) { fc.Step(dt); ofc.Step(dt); Physics.Simulate(dt); }
                }
                input.value = default;
                RunBoth(100);
                input.value = new FighterInput { grabLeft = true, grabRight = true };
                RunBoth(100);
                bool grabbedOther = fc.LeftHand.GrabbedFighter == ofc || fc.RightHand.GrabbedFighter == ofc;
                Check("grab", grabbedOther, $"left={fc.LeftHand.IsGrabbing} right={fc.RightHand.IsGrabbing} grabbedFighter={grabbedOther}");

                float otherZ0 = other[Part.Hips].position.z;
                input.value = new FighterInput { grabLeft = true, grabRight = true, move = new Vector2(0f, -1f) };
                RunBoth(200);
                float dragged = otherZ0 - other[Part.Hips].position.z;
                Check("drag", dragged > 1.0f, $"opponent dragged {dragged:F2}m, still holding={fc.LeftHand.IsGrabbing || fc.RightHand.IsGrabbing}");

                input.value = default;
                RunBoth(20);
                Check("release", !fc.LeftHand.IsGrabbing && !fc.RightHand.IsGrabbing, "hands released after button up");

                // 9) 악력: 계속 잡고 있으면 결국 손이 풀리고, 쉬면 다시 잡을 수 있다
                fc.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
                ofc.ResetFighter(new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
                RunBoth(50);
                input.value = new FighterInput { grabLeft = true, grabRight = true };
                float firstRelease = -1f;
                for (int s = 0; s < 800 && firstRelease < 0f; s++)
                {
                    fc.Step(dt); ofc.Step(dt); Physics.Simulate(dt);
                    if (fc.IsExhausted) firstRelease = s * dt;
                }
                fc.Step(dt); ofc.Step(dt); Physics.Simulate(dt);
                bool releasedWhenExhausted = fc.IsExhausted && !fc.LeftHand.IsGrabbing && !fc.RightHand.IsGrabbing;
                Check("stamina exhaust", firstRelease > 2f && firstRelease < 6f && releasedWhenExhausted,
                    $"exhausted after {firstRelease:F1}s holding, hands free={releasedWhenExhausted}");
                input.value = default;
                RunBoth(150);
                Check("stamina recover", !fc.IsExhausted, $"stamina={fc.Stamina:F2} exhausted={fc.IsExhausted}");

                // 10) 들어올리기: 상대를 잡고 팔을 위로 → 상대가 뜨고 균형을 잃는다
                fc.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
                ofc.ResetFighter(new Vector3(0f, 0.05f, 1.0f), Quaternion.Euler(0f, 180f, 0f));
                input.value = default;
                RunBoth(50);
                input.value = new FighterInput { grabLeft = true, grabRight = true };
                RunBoth(80);
                float oppHip0 = other[Part.Hips].position.y;
                input.value = new FighterInput { grabLeft = true, grabRight = true, armPitch = 1f };
                float maxRise = 0f; bool wasLifted = false;
                for (int s = 0; s < 120; s++)
                {
                    fc.Step(dt); ofc.Step(dt); Physics.Simulate(dt);
                    maxRise = Mathf.Max(maxRise, other[Part.Hips].position.y - oppHip0);
                    wasLifted |= ofc.IsLifted;
                }
                Check("lift", wasLifted && maxRise > 0.15f, $"opponent lifted={wasLifted}, hip rise={maxRise:F2}m");
                input.value = default;
                RunBoth(100);
                Check("lift ends", !ofc.IsLifted && other[Part.Hips].position.y > 0.6f, $"opp hipY after release={other[Part.Hips].position.y:F2}");

                // 11) 바닥 잡기: 팔을 아래로 뻗으면 숙여서 바닥을 잡는다
                Object.DestroyImmediate(other.gameObject);
                fc.ResetFighter(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
                input.value = default;
                Run(50);
                input.value = new FighterInput { grabRight = true, armPitch = -1f };
                Run(150);
                Check("floor grab", fc.RightHand.IsGrabbingWorld, $"grabbing world={fc.RightHand.IsGrabbingWorld}, tipY={fc.RightHand.TipPosition.y:F2} hipY={hips.position.y:F2}");
                input.value = default;
                Run(150);
                Check("stand after floor grab", !fc.RightHand.IsGrabbing && hips.position.y > 0.7f, $"hipY={hips.position.y:F2}");

                // 12) 가장자리 매달리기 → 기어오르기
                Object.DestroyImmediate(floor);
                var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ledge.transform.localScale = new Vector3(10f, 1f, 10f);
                ledge.transform.position = new Vector3(-5f, -0.5f, 0f); // 윗면 y=0, 가장자리 x=0
                // 가장자리를 등지고 서서(-X를 봄) 뒤로 걸어 떨어지며 앞(벽 쪽)으로 손을 뻗는다
                fc.ResetFighter(new Vector3(-0.5f, 0.05f, 0f), Quaternion.LookRotation(Vector3.left));
                input.value = default;
                Run(50);
                input.value = new FighterInput { move = new Vector2(1f, 0f), aim = new Vector2(-1f, 0f), grabLeft = true, grabRight = true };
                bool hung = false;
                for (int s = 0; s < 250; s++)
                {
                    fc.Step(dt); Physics.Simulate(dt);
                    // 사람처럼: 발이 떨어지면 이동키를 놓고 가장자리 쪽으로 손을 뻗은 채 떨어진다
                    if (!fc.IsGrounded && !hung) input.value = new FighterInput { aim = new Vector2(-1f, 0f), grabLeft = true, grabRight = true };
                    if (fc.IsHanging && !hung) { hung = true; input.value = new FighterInput { aim = new Vector2(-1f, 0f), grabLeft = true, grabRight = true }; }
                }
                float hangY = hips.position.y;
                Check("hang on ledge", hung && fc.IsHanging && hangY > -1.5f && hangY < 0.3f, $"hanging={fc.IsHanging}, hipY={hangY:F2}");

                input.value = new FighterInput { aim = new Vector2(-1f, 0f), grabLeft = true, grabRight = true, jump = true };
                fc.Step(dt); Physics.Simulate(dt);
                input.value = default;
                Run(200);
                Check("climb up", hips.position.y > 0.6f && hips.position.x < -0.1f, $"after climb hip=({hips.position.x:F2}, {hips.position.y:F2})");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SelfTest] EXCEPTION " + e);
                ok = false;
            }
            finally
            {
                Physics.simulationMode = prevMode;
            }

            Debug.Log(ok ? "[SelfTest] ALL PASSED" : "[SelfTest] SOME CHECKS FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }

        class ScriptedInput : IFighterInputSource
        {
            public FighterInput value;
            public FighterInput ReadInput() => value;
        }
    }
}
