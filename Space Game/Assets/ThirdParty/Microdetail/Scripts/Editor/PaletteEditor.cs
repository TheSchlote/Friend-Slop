using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Microdetail
{
    [CustomEditor(typeof(Palette))]
    public class PaletteEditor : Editor
    {
        private ReorderableList layersList;
        private Editor previousEditor;

        public Terrain Context
        {
            get;
            set;
        }
        
        private Layer GetSelectedLayer()
        {
            if (layersList.index < 0 || layersList.index >= layersList.count)
                return null;
            
            var palette = target as Palette;
            return palette.GetLayer(layersList.index);
        }

        public override void OnInspectorGUI()
        {
            var palette = target as Palette;
            serializedObject.Update();
            if (layersList == null)
            {
                layersList = new ReorderableList(serializedObject, serializedObject.FindProperty("layers"), true, true, true, true);

                layersList.drawHeaderCallback = (Rect rect) =>
                    {
                        EditorGUI.LabelField(rect, "Layers");
                    };

                layersList.onAddCallback += _ =>
                    {
                        palette.AddLayer();
                        serializedObject.Update();
                        serializedObject.ApplyModifiedProperties();
                    };

                layersList.onReorderCallbackWithDetails += (list, index, newIndex) =>
                    {
                        palette.MoveLayer(index, newIndex);
                    };

                layersList.onRemoveCallback += _ =>
                    {
                        var layer = palette.GetLayer(layersList.index);
                        palette.RemoveLayer(layer);
                        serializedObject.Update();
                        serializedObject.ApplyModifiedProperties();
                    };

                layersList.elementHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                
                layersList.drawElementCallback += (Rect rect, int index, bool isActive, bool isFocused) =>
                    {
                        var layer = palette.GetLayer(index);
                        
                        rect.y += EditorGUIUtility.standardVerticalSpacing;
                        rect.height = EditorGUIUtility.singleLineHeight;
                        var enabledToggleRect = rect;
                        enabledToggleRect.width = 20;
                        var visibleToggleRect = enabledToggleRect;
                        visibleToggleRect.x += enabledToggleRect.width + EditorGUIUtility.standardVerticalSpacing;

                        var wasEnabled = layer.Enabled;
                        layer.Enabled = EditorGUI.Toggle(enabledToggleRect, GUIContent.none, layer.Enabled);
#if DISPLAY_VISIBLE_TOGGLE
                        layer.Visible = EditorGUI.Toggle(visibleToggleRect, GUIContent.none, layer.Visible);
#endif
                        if (wasEnabled != layer.Enabled)
                        {
                            serializedObject.Update();
                            serializedObject.ApplyModifiedProperties();
                        }
                        
                        var layerNameRect = rect;
                        var nameShift = enabledToggleRect.width
#if DISPLAY_VISIBLE_TOGGLE
                                        + 20;
#else
                                            ;
#endif
                        layerNameRect.x += nameShift;
                        layerNameRect.width -= nameShift;

                        var lastName = layer.name;
                        layer.name = EditorGUI.DelayedTextField(layerNameRect, layer.name);
                        if (lastName != layer.name)
                        {
                            serializedObject.Update();
                            serializedObject.ApplyModifiedProperties();
                        }
                    };

                layersList.onSelectCallback += _ =>
                    {
                        palette.SelectedIndex = layersList.selectedIndices[0];
                        serializedObject.Update();
                        serializedObject.ApplyModifiedProperties();
                    };
            }

            layersList.Select(palette.SelectedIndex, false);
            layersList.DoLayoutList();

            var currentSelected = GetSelectedLayer();
            if (currentSelected != null)
            {
                Editor.CreateCachedEditor(currentSelected, typeof(LayerEditor), ref previousEditor);

                previousEditor.OnInspectorGUI();
            }

#if DRAW_ADDITIONAL_MICRODETAIL_INFO
            if (Context == null) 
                return;
            
            DrawInvalidTextures();
#endif
        }
        
#if DRAW_ADDITIONAL_MICRODETAIL_INFO
        private void DrawInvalidTextures()
        {
            var renderer = RendererUtility.GetMicrodetailRenderer(Context);
            if (renderer == null)
                return;

            var textures = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(Context.terrainData));
            foreach (var texture in textures)
            {
                if (texture is Texture2D texture2D)
                {
                    if (!texture2D.name.StartsWith("Tint") &&
                        !texture2D.name.StartsWith("Density"))
                        continue;
                    
                    if (renderer.IsValidTexture(texture2D))
                        continue;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(texture2D.name);
                    if (GUILayout.Button(string.Empty, "OL Minus"))
                        Undo.DestroyObjectImmediate(texture2D);
                    
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
#endif
    }
}