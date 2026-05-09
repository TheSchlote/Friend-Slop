using System;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Microdetail
{
    [CustomEditor(typeof(MicrodetailAsset), true)]
    [CanEditMultipleObjects]
    public class MicrodetailAssetEditor : Editor
    {
        private static Thread mainThread;
        
        private ReorderableList reorderableList;
        private static Type[] modulePropertyTypes;
        private static string[] modulePropertyTypeNames;
        
        private Editor moduleEditor;

        private void OnEnable()
        {
            mainThread ??= Thread.CurrentThread;
            modulePropertyTypes ??= AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(Module).IsAssignableFrom(type) && !type.IsAbstract)
                .ToArray();

            var asset = target as MicrodetailAsset;
            if (asset == null)
                return;

            modulePropertyTypeNames ??= modulePropertyTypes.Select(type => type.Name).ToArray();

            var modulePropertiesProperty = serializedObject.FindProperty("modules");

            reorderableList = new ReorderableList(serializedObject, modulePropertiesProperty, true, true, true, true)
                {
                    drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Modules"),

                    drawElementCallback = (rect, index, isActive, isFocused) =>
                        {
                            var element = modulePropertiesProperty.GetArrayElementAtIndex(index);
                            if (element.objectReferenceValue == null) 
                                return;
                            
                            EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), ObjectNames.NicifyVariableName(element.objectReferenceValue.GetType().Name));
                        },
                    onAddDropdownCallback = (buttonRect, list) =>
                        {
                            var menu = new GenericMenu();
                            for (var i = 0; i < modulePropertyTypes.Length; i++)
                            {
                                var moduleType = modulePropertyTypes[i];
                                var typeName = modulePropertyTypeNames[i];
                                menu.AddItem(new GUIContent(typeName), false, () => AddModuleProperty(moduleType));
                            }
                            menu.ShowAsContext();
                        },
                    onRemoveCallback = list =>
                        {
                            Undo.IncrementCurrentGroup();
                            Undo.SetCurrentGroupName("Module removed");
                            Undo.RecordObject(asset, "Remove module");
                            var moduleToRemove = asset.GetModule(list.index);
                            asset.RemoveModule(moduleToRemove);
                            Undo.DestroyObjectImmediate(moduleToRemove);
                            AssetDatabase.SaveAssets();

                            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                        }
                };
            }
        
        private void AddModuleProperty(Type moduleType)
        {
            if (!AssetDatabase.Contains(target))
            {
                Debug.LogError("The target is not persistent. Ensure it is saved to disk.");
                return;
            }

            serializedObject.Update();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add module");
            var newModule = CreateInstance(moduleType) as Module;
            Undo.RegisterCreatedObjectUndo(newModule, "Add module");
            Undo.RegisterFullObjectHierarchyUndo(target, "Add module");
            if (newModule != null)
            {
                newModule.name = ObjectNames.NicifyVariableName(moduleType.Name);
                (target as MicrodetailAsset)?.AddModule(newModule);
                serializedObject.Update();

                AssetDatabase.AddObjectToAsset(newModule, target);
            }

            AssetDatabase.SaveAssets();
            
            serializedObject.ApplyModifiedProperties();
            
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Refresh preview"))
                foreach (var entry in targets)
                    PreviewRenderingUtility.GetPreviewTexture(entry as MicrodetailAsset, true, true);

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "modules");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Module parameters:", EditorStyles.boldLabel);
            reorderableList.DoLayoutList();

            if (reorderableList.index >= 0 && reorderableList.index < reorderableList.count)
            {
                EditorGUILayout.Space();

                var asset = target as MicrodetailAsset;
                if (asset == null)
                {
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                
                var module = asset.GetModule(reorderableList.index);
                if (module == null)
                {
                    serializedObject.ApplyModifiedProperties();
                    return;
                }

                EditorGUI.indentLevel++;
                CreateCachedEditor(module, null, ref moduleEditor);
                moduleEditor.serializedObject.Update();
                moduleEditor.OnInspectorGUI();
                moduleEditor.serializedObject.ApplyModifiedProperties();
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            try
            {
                var result = PreviewRenderingUtility.GetPreviewTexture(AssetDatabase.LoadAssetAtPath<MicrodetailAsset>(assetPath), false, false);
                return result == null ? null : Instantiate(result);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }
        }
    }
}