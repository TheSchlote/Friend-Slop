using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Unity.Mathematics;
using Random = UnityEngine.Random;

namespace Microdetail
{
    [CustomEditor(typeof(Layer))]
    public class LayerEditor : Editor
    {
        private SerializedProperty entriesProperty;
        private ReorderableList reorderableList;
        private Editor entryEditor;

        private void OnEnable()
        {
            try
            {
                entriesProperty = serializedObject.FindProperty("entries");
            }
            catch
            {
                return;
            }

            reorderableList = new ReorderableList(serializedObject, entriesProperty, true, true, true, true)
                {
                    drawHeaderCallback = rect =>
                        {
                            EditorGUI.LabelField(rect, "Layer Entries");
                        },
                    drawElementCallback = (rect, index, isActive, isFocused) =>
                        {
                            var entryProperty = entriesProperty.GetArrayElementAtIndex(index);
                            var assetProperty = entryProperty.FindPropertyRelative("asset");

                            var previewRect = new Rect(10, rect.y, 100, EditorGUIUtility.singleLineHeight * 2);
                            var fieldRect = new Rect(rect.x + 50, rect.y + EditorGUIUtility.singleLineHeight * 0.5f, rect.width - 50, EditorGUIUtility.singleLineHeight);

                            DrawAssetPreview(previewRect, assetProperty);

                            EditorGUI.PropertyField(fieldRect, assetProperty, GUIContent.none);
                        },
                    onAddCallback = list =>
                        {
                            entriesProperty.arraySize++;
                            var newEntry = entriesProperty.GetArrayElementAtIndex(entriesProperty.arraySize - 1);
                            newEntry.FindPropertyRelative("samplesPerUnitArea").floatValue = 128;
                            newEntry.FindPropertyRelative("asset").objectReferenceValue = null;
                            newEntry.FindPropertyRelative("seed").SetFloat2FromVector(new float2(Random.Range(-1000.0f, 1000.0f), Random.Range(-1000.0f, 1000.0f)));
                        },
                    onRemoveCallback = list =>
                        {
                            if (list.index >= 0)
                                entriesProperty.DeleteArrayElementAtIndex(list.index);
                        },
                    drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                        {
                            if (isActive)
                                EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 1.0f, 0.2f));
                        },
                    elementHeightCallback = index =>
                        {
                            return EditorGUIUtility.singleLineHeight * 2;
                        }
                };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            reorderableList.DoLayoutList();
            
            if (reorderableList.selectedIndices.Count == 0 && reorderableList.count > 0)
                reorderableList.Select(0);

            if (reorderableList.index >= 0 && reorderableList.index < entriesProperty.arraySize)
                DrawSelectedEntryDetails();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAssetPreview(Rect rect, SerializedProperty assetProperty)
        {
            var asset = assetProperty.objectReferenceValue as MicrodetailAsset;
            var previewTexture = asset == null ? Texture2D.linearGrayTexture : AssetPreview.GetAssetPreview(asset);
            if (previewTexture == null)
                previewTexture = Texture2D.linearGrayTexture;

            GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit);
        }

        private void DrawSelectedEntryDetails()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Selected Entry Details", EditorStyles.boldLabel);

            var selectedEntry = entriesProperty.GetArrayElementAtIndex(reorderableList.index);
            var assetProperty = selectedEntry.FindPropertyRelative("asset");

            var size = 100;
            var rect = GUILayoutUtility.GetRect(size, size, size, size);
            rect.width = size;
            
            DrawAssetPreview(rect, assetProperty);
            
            var samplesCount = selectedEntry.FindPropertyRelative("samplesPerUnitArea");
            EditorGUILayout.PropertyField(samplesCount);
            if (samplesCount.floatValue < 0.0f)
                samplesCount.floatValue = 0.0f;

            var maxSamples = 8192.0f;
            if (samplesCount.floatValue > maxSamples)
                samplesCount.floatValue = maxSamples;
            
            if (reorderableList.index < 0)
                return;
            
            CreateCachedEditor(assetProperty.objectReferenceValue, null, ref entryEditor);
            if (entryEditor == null)
                return;

            entryEditor.serializedObject.ApplyModifiedProperties();
            entryEditor.OnInspectorGUI();
            entryEditor.serializedObject.ApplyModifiedProperties();
        }
    }
}