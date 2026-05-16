using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Microdetail
{
    public class RendererData : ScriptableObject
    {
        [SerializeField] private List<LayerRenderer> layers = new List<LayerRenderer>();

        public List<LayerRenderer> Layers => layers;

        private void OnEnable()
        {
            name = "Microdetail renderer data";
        }

        public LayerRenderer CreateLayer(Layer layer)
        {
            if (layers.Any(x => x.Layer == layer))
                return layers.First(x => x.Layer == layer);
            
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);            
#endif

            var result = new LayerRenderer(layer, Guid.NewGuid().ToString());
            layers.Add(result);
            
            return result;
        }

        public void AddLayer(LayerRenderer renderer)
        {
            if (layers.Find(x => x.Layer == renderer.Layer) != null)
            {
                Debug.LogError("Layer already exists");
                return;
            }
            
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);            
#endif
            
            layers.Add(renderer);
        }

        public LayerRenderer GetLayer(Layer layer)
        {
            return layers.FirstOrDefault(x => x.Layer == layer);
        }

        public void RemoveLayer(Layer layer)
        {
            var renderer = GetLayer(layer);
            if (renderer == null)
                return;
            
            renderer.Dispose();
            layers.Remove(renderer);
        }

        public void UpdateWithPersistent()
        {
            foreach (var layer in layers)
                layer.UpdateWithPersistentMaps();
        }
        
#if UNITY_EDITOR
        public void UpdateWithCurrent()
        {
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            var terrainData = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);

            foreach (var layer in layers)
            {
                layer.UpdatePersistentWithCurrent(terrainData);
                layer.SaveDirty();
            }
        }
#endif
    }
}