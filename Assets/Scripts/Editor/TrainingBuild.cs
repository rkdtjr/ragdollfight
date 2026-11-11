using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFight.EditorTools
{
    /// <summary>
    /// 학습용 씬(Assets/Scenes/Training.unity)을 만들고 윈도우 실행 파일로 빌드한다.
    /// mlagents-learn 이 이 실행 파일을 띄워서 학습한다 (에디터에서 Play를 누를 필요 없음).
    /// </summary>
    public static class TrainingBuild
    {
        const string ScenePath = "Assets/Scenes/Training.unity";

        [MenuItem("RagdollFight/Create Training Scene")]
        public static void CreateTrainingScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("TrainingManager").AddComponent<TrainingManager>();

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(25f, 45f, -20f);
            cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        [MenuItem("RagdollFight/Build Training Env")]
        public static void Build()
        {
            CreateTrainingScene();
            // ML-Agents의 gRPC DLL이 한글 경로에서 열리지 않아서 영문 경로(%LOCALAPPDATA%)에 빌드한다.
            string output = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                                         "ragdollfight", "env", "RagdollTrain.exe");
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildOutput") output = args[i + 1];

            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 960;
            PlayerSettings.defaultScreenHeight = 540;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.usePlayerLog = true;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[TrainingBuild] {report.summary.result} -> {output} ({report.summary.totalErrors} errors)");
            if (EditorApplication.isPlaying == false && Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
