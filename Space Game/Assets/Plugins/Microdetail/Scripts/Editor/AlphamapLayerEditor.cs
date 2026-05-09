using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    namespace Microdetail
    {
        [CustomEditor(typeof(AlphamapProceduralLayer))]
        public class AlphamapProceduralLayerEditor : Editor
        {
            private SerializedProperty strength;
            private SerializedProperty layerIndex;
            private SerializedProperty layer;
            private SerializedProperty maskTexture;
            private SerializedProperty maskTextureScale;
            private SerializedProperty thresholdValue;

            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                strength = serializedObject.FindProperty("strength");
                layerIndex = serializedObject.FindProperty("layerIndex");
                layer = serializedObject.FindProperty("layer");
                maskTexture = serializedObject.FindProperty("maskTexture");
                maskTextureScale = serializedObject.FindProperty("maskTextureScale");
                thresholdValue = serializedObject.FindProperty("threshold");

                EditorGUILayout.LabelField("Alphamap Layer Settings", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(strength);

                var proceduralLayerTarget = (AlphamapProceduralLayer)target;
                var targetTerrain = proceduralLayerTarget.GetComponent<Terrain>();

                if (targetTerrain != null && targetTerrain.terrainData != null)
                {
                    var terrainLayers = targetTerrain.terrainData.terrainLayers;
                    var terrainLayerNames = new string[terrainLayers.Length];

                    for (var i = 0; i < terrainLayers.Length; i++)
                        terrainLayerNames[i] = terrainLayers[i] != null ? terrainLayers[i].name : $"Layer {i}";

                    var selectedLayerIndex = EditorGUILayout.Popup("Terrain layer", layerIndex.intValue, terrainLayerNames);
                    layerIndex.intValue = selectedLayerIndex;
                }
                else
                    EditorGUILayout.PropertyField(layerIndex);

                EditorGUILayout.PropertyField(thresholdValue);

                if (GUILayout.Button(layer.objectReferenceValue ? layer.objectReferenceValue.name : "Select Layer", EditorStyles.popup))
                {
                    LoadLayersFromPalettes(out var allLayers, out var layerNames);

                    var menu = new GenericMenu();
                    var currentIndex = System.Array.IndexOf(allLayers, layer.objectReferenceValue);

                    for (var i = 0; i < allLayers.Length; i++)
                    {
                        var capturedIndex = i;
                        menu.AddItem(new GUIContent(layerNames[i]), i == currentIndex, () =>
                        {
                            layer.objectReferenceValue = allLayers[capturedIndex];
                            serializedObject.ApplyModifiedProperties();
                        });
                    }
                    menu.ShowAsContext();
                }

                EditorGUILayout.PropertyField(maskTexture);
                EditorGUILayout.PropertyField(maskTextureScale);

                EditorGUILayout.BeginHorizontal();

                if (maskTexture.objectReferenceValue is Texture2D maskPreviewTexture)
                {
                    var previewRect = GUILayoutUtility.GetAspectRect(1.0f, GUILayout.MaxWidth(128));
                    EditorGUI.DrawPreviewTexture(previewRect, maskPreviewTexture, null, ScaleMode.ScaleToFit);
                }

                if (targetTerrain != null && targetTerrain.terrainData != null)
                {
                    var alphaTexture = targetTerrain.terrainData.GetAlphamapTexture(layerIndex.intValue / 4);
                    if (alphaTexture is Texture2D alphaPreviewTexture)
                    {
                        var previewRect = GUILayoutUtility.GetAspectRect(1.0f, GUILayout.MaxWidth(128));
                        EditorGUI.DrawPreviewTexture(previewRect, alphaPreviewTexture, null, ScaleMode.ScaleToFit);
                    }
                }

                EditorGUILayout.EndHorizontal();

                serializedObject.ApplyModifiedProperties();
            }

            private void LoadLayersFromPalettes(out Layer[] allLayers, out string[] layerNames)
            {
                var paletteGuids = AssetDatabase.FindAssets("t:Microdetail.Palette");
                var palettes = paletteGuids.Select(guid => AssetDatabase.LoadAssetAtPath<Palette>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
                allLayers = palettes.SelectMany(palette => Enumerable.Range(0, palette.LayersCount).Select(i => palette.GetLayer(i))).ToArray();
                layerNames = palettes.SelectMany(palette => Enumerable.Range(0, palette.LayersCount)
                    .Select(i =>
                    {
                        var currentLayer = palette.GetLayer(i);
                        var layerName = currentLayer != null ? currentLayer.name : "Unnamed";
                        return $"{palette.name}/{layerName}";
                    })).ToArray();
            }
        }
    }
}