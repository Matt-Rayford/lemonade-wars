using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LemonadeWars.UnityEditorTools
{
    /// <summary>
    /// One-click standalone builds for local multiplayer testing. Ensures the (empty)
    /// boot scene exists — the app spawns itself via RuntimeInitializeOnLoadMethod — and
    /// configures a windowed, resizable player so instances sit side-by-side.
    /// </summary>
    public static class BuildTools
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string OutputPath = "Build/LemonadeWars.app";

        [MenuItem("Lemonade Wars/Build macOS Player")]
        public static void BuildMac()
        {
            EnsureBootScene();

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // keep sockets alive when unfocused

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Build {report.summary.result}: {OutputPath} " +
                      $"({report.summary.totalSize / (1024 * 1024)} MB)");
        }

        /// <summary>Create and register the minimal boot scene if it does not exist yet.</summary>
        private static void EnsureBootScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
