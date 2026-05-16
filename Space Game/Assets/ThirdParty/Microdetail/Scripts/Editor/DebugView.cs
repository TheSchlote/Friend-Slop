using System;
using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    public class DebugView : EditorWindow
    {
        [MenuItem("Tools/Microdetail/Debug View")]
        private static void ShowDebugView()
        {
            GetWindow<DebugView>("Debug View");
        }
        
        public void OnGUI()
        {
            DebugProperties.UpdateBuffers = EditorGUILayout.Toggle("Update buffers", DebugProperties.UpdateBuffers);
            DebugProperties.RenderFull = EditorGUILayout.Toggle("Render full", DebugProperties.RenderFull);
            DebugProperties.GetReadback = EditorGUILayout.Toggle("Get readback", DebugProperties.GetReadback);
            DebugProperties.CollectData = EditorGUILayout.Toggle("Collect data", DebugProperties.CollectData);

            if (GUILayout.Button("Reload placement shader"))
            {
                var renderers = FindObjectsByType<MicrodetailRenderer>(FindObjectsSortMode.None);
                foreach (var renderer in renderers)
                    renderer.ReloadComputeShader();
            }
            
            if (!DebugProperties.CollectData)
                return;

            foreach (var data in DebugProperties.LayersData)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Layer {data.Key.Layer.name}");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Entries count {data.Value.EntriesCount}");
                EditorGUILayout.LabelField($"Expected count {data.Value.ExpectedCount}");
                EditorGUILayout.LabelField($"Buffer size {data.Value.BufferSize} mb");
                EditorGUI.indentLevel--;
                EditorGUILayout.EndHorizontal();
            }
        }
        
        private void Update()
        {
            Repaint();
            DebugProperties.ClearData();
        }
    }
}