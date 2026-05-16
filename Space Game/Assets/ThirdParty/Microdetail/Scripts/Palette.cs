using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Microdetail
{
    [CreateAssetMenu(fileName = "Palette", menuName = "Microdetail/Palette")]
    public class Palette : ScriptableObject
    {
        [SerializeField] private int selectedIndex = 0;
        [SerializeField] private List<Layer> layers = new List<Layer>();

        public int LayersCount => layers.Count;
        
        public int SelectedIndex
        {
            get => selectedIndex;
            set => selectedIndex = value;
        }

        public Layer SelectedLayer => SelectedIndex < 0 || SelectedIndex >= layers.Count ? null : layers[SelectedIndex];
        
        public Layer AddLayer()
        {
            var layer = ScriptableObject.CreateInstance<Layer>();
            layer.name = "Layer";

#if UNITY_EDITOR
            UnityEditor.Undo.IncrementCurrentGroup();
            var groupIndex = UnityEditor.Undo.GetCurrentGroup();

            UnityEditor.Undo.RegisterCreatedObjectUndo(layer, "Create layer");

            UnityEditor.Undo.RecordObject(this, "Add layer to palette");
#endif

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.AddObjectToAsset(layer, this);
            UnityEditor.EditorUtility.SetDirty(this);
#endif

            layers.Add(layer);

#if UNITY_EDITOR
            UnityEditor.Undo.CollapseUndoOperations(groupIndex);
#endif

            return layer;
        }

        public Layer GetLayer(int index)
        {
            return layers[index];
        }

        public void MoveLayer(int oldIndex, int newIndex)
        {
            (layers[oldIndex], layers[newIndex]) = (layers[newIndex], layers[oldIndex]);
        }

        public bool HasLayer(Layer layer)
        {
            return layers.Contains(layer);
        }

        public void RemoveLayer(Layer layer)
        {
            if (!layers.Contains(layer))
            {
                Debug.LogError("Trying to delete layer that is not the part of the palette.");
                return;
            }
            
#if UNITY_EDITOR
            UnityEditor.Undo.IncrementCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Delete layer");
            var groupIndex = UnityEditor.Undo.GetCurrentGroup();
            
            var rendererData = UnityEditor.AssetDatabase.FindAssets("t:RendererData").ToList()
                .ConvertAll(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .ConvertAll(UnityEditor.AssetDatabase.LoadAssetAtPath<RendererData>);
                        
            foreach (var data in rendererData)
            {
                if (data == null)
                    continue;
                
                if (data.GetLayer(layer) == null)
                    continue;

                UnityEditor.Undo.RecordObject(data, "Delete layer");
                data.RemoveLayer(layer);
                UnityEditor.EditorUtility.SetDirty(data);
            }
            
            UnityEditor.Undo.RegisterCompleteObjectUndo(this, "Delete layer");
#endif
            
            layers.Remove(layer);
            
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(layer);
            UnityEditor.Undo.CollapseUndoOperations(groupIndex);
#endif
        }
    }
}
