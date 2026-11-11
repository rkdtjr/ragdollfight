using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFight.EditorTools
{
    /// <summary>
    /// 캐릭터 모습 확인용 스크린샷. 두 파이터를 세우고(한 명은 팔을 뻗게) PNG로 저장한다.
    /// 커맨드라인: -executeMethod RagdollFight.EditorTools.Snapshot.Take -snapshotOut path.png  (-nographics 없이)
    /// </summary>
    public static class Snapshot
    {
        class Scripted : IFighterInputSource
        {
            public FighterInput value;
            public FighterInput ReadInput() => value;
        }

        [MenuItem("RagdollFight/Take Character Snapshot")]
        public static void Take()
        {
            string output = "snapshot.png";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-snapshotOut") output = args[i + 1];

            var prevMode = Physics.simulationMode;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                Arena.Create(null, 5f);
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                light.shadows = LightShadows.Soft;
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

                FighterController Make(string n, Vector3 p, float yaw, Color c, FighterInput input)
                {
                    var rd = Ragdoll.Create(n, p, Quaternion.Euler(0f, yaw, 0f), c);
                    var fc = rd.gameObject.AddComponent<FighterController>();
                    fc.Init();
                    fc.autoStep = false;
                    fc.InputSource = new Scripted { value = input };
                    return fc;
                }
                var a = Make("A", new Vector3(-0.8f, 0.05f, 0f), 90f, new Color(0.95f, 0.45f, 0.35f),
                    new FighterInput { grabLeft = true, grabRight = true, armPitch = 0.3f, aim = new Vector2(1f, 0f) });
                var b = Make("B", new Vector3(0.8f, 0.05f, 0f), -90f, new Color(0.35f, 0.6f, 0.95f),
                    new FighterInput { aim = new Vector2(-1f, 0f) });
                for (int s = 0; s < 150; s++) { a.Step(0.01f); b.Step(0.01f); Physics.Simulate(0.01f); }

                var camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
                cam.fieldOfView = 40f;
                cam.transform.position = new Vector3(1.2f, 1.7f, -3.6f);
                cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));

                var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(output, tex.EncodeToPNG());
                RenderTexture.active = null;
                Debug.Log("[Snapshot] saved " + output);
            }
            finally
            {
                Physics.simulationMode = prevMode;
            }
        }
    }
}
