using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LemonadeWars.UnityEditorTools
{
    /// <summary>
    /// One-shot URP configuration (Unity 6.5 deprecates the Built-In Render Pipeline).
    /// Creates a URP asset with a 2D renderer and assigns it to Graphics + every quality
    /// level. Idempotent — safe to run again. Runs headless via:
    ///   Unity -batchmode -executeMethod LemonadeWars.UnityEditorTools.UrpSetup.Configure -quit
    /// or from the menu: Lemonade Wars > Configure URP.
    /// </summary>
    public static class UrpSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string RendererPath = SettingsFolder + "/Renderer2D.asset";
        private const string PipelinePath = SettingsFolder + "/UrpPipeline.asset";

        [MenuItem("Lemonade Wars/Configure URP")]
        public static void Configure()
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            var pipeline = LoadOrCreatePipeline(LoadOrCreateRenderer());
            GraphicsSettings.defaultRenderPipeline = pipeline;
            AssignToEveryQualityLevel(pipeline);

            AssetDatabase.SaveAssets();
            Debug.Log("URP configured: " + PipelinePath);
        }

        private static Renderer2DData LoadOrCreateRenderer()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }
            return rendererData;
        }

        private static UniversalRenderPipelineAsset LoadOrCreatePipeline(Renderer2DData rendererData)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            return pipeline;
        }

        /// <summary>Setting a level's pipeline means switching to that level, so the active one is restored after.</summary>
        private static void AssignToEveryQualityLevel(UniversalRenderPipelineAsset pipeline)
        {
            int activeLevel = QualitySettings.GetQualityLevel();
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(activeLevel, applyExpensiveChanges: false);
        }
    }
}
