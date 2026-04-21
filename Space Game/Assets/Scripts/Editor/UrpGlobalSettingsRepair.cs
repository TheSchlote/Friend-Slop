using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.EditorTools
{
    public static class UrpGlobalSettingsRepair
    {
        private const string AssetPath = "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset";
        private const string PipelineTypeName = "UnityEngine.Rendering.Universal.UniversalRenderPipeline, Unity.RenderPipelines.Universal.Runtime";
        private const string GlobalSettingsTypeName = "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings, Unity.RenderPipelines.Universal.Runtime";

        [MenuItem("Tools/Friend Slop/Repair URP Global Settings")]
        public static void RepairFromMenu()
        {
            Repair();
        }

        public static void RepairFromCommandLine()
        {
            Repair();
        }

        private static void Repair()
        {
            var pipelineType = Type.GetType(PipelineTypeName);
            var globalSettingsType = Type.GetType(GlobalSettingsTypeName);
            if (pipelineType == null || globalSettingsType == null)
            {
                throw new InvalidOperationException("Unable to locate the URP pipeline or global settings types.");
            }

            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(pipelineType, null);
            AssetDatabase.DeleteAsset(AssetPath);

            var createdAsset = RenderPipelineGlobalSettingsUtils.Create(globalSettingsType, AssetPath);
            if (createdAsset == null)
            {
                throw new InvalidOperationException($"Failed to recreate {AssetPath}.");
            }

            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(pipelineType, createdAsset);
            EditorUtility.SetDirty(createdAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Recreated URP global settings asset at {AssetPath}.");
        }
    }
}
