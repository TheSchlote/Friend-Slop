using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Microdetail
{
    [ExecuteAlways]
    public class SetupWizard
    {
        private static RenderPipelineType currentRenderPipeline = RenderPipelineType.None;
        
        public enum RenderPipelineType
        {
            None,
            BuiltIn,
            URP,
            HDRP,
            Custom
        }
        
        private static RenderPipelineType GetCurrentRenderPipeline()
        {
            var currentPipeline = GraphicsSettings.currentRenderPipeline;
            if (currentPipeline == null) 
                return RenderPipelineType.BuiltIn;

            var pipelineName = currentPipeline.GetType().Name;
            if (pipelineName.Contains("Universal")) 
                return RenderPipelineType.URP;
            
            if (pipelineName.Contains("HD")) 
                return RenderPipelineType.HDRP;
            
            return RenderPipelineType.Custom;
        }
        
        [InitializeOnLoadMethod]
        public static void Initialize()
        {
            EditorApplication.update += UpdateShaders;
        }

        private static void UpdateShaders()
        {
            if (Application.isPlaying)
                return;
            
            var currentPipeline = GetCurrentRenderPipeline();
            if (currentPipeline == currentRenderPipeline)
                return;
            
            if (currentPipeline == RenderPipelineType.BuiltIn ||
                currentPipeline == RenderPipelineType.Custom)
            {
                Debug.LogError("Microdetail doesn't support built-in and custom render pipelines.");
                return;
            }

            var path = Path.Combine("Assets", "Plugins", "Microdetail", "Pipelines");

            switch (currentPipeline)
            {
                case RenderPipelineType.URP:
                    path = Path.Combine(path, "MicrodetailURPSupport.unitypackage");
                    break;
                case RenderPipelineType.HDRP:
                    path = Path.Combine(path, "MicrodetailHDRPSupport.unitypackage");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Microdetail] Shader support package not found at {path}. Skipping import.");
                return;
            }

            try
            {
                AssetDatabase.ImportPackage(path, false);
                currentRenderPipeline = currentPipeline;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Microdetail] Could not import shader support package: {e.Message}");
            }
        }
    }
}