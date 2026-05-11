using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    public static class ClearUtility
    {
        public static void Clear(Layer layer, Func<LayerRenderer, MapSet> setGetter, Texture2D clearTexture, string name)
        {
            var renderers = Resources.FindObjectsOfTypeAll<MicrodetailRenderer>().ToList();
            renderers.RemoveAll(x => !x.gameObject.scene.IsValid());
            
            Undo.IncrementCurrentGroup();
            foreach (var renderer in renderers)
            {
                var parentTerrain = renderer.GetComponentInParent<Terrain>();
                if (parentTerrain == null)
                    continue;
                
                var layerRenderer = renderer.GetLayer(layer);
                if (layerRenderer == null)
                    continue;
                
                var map = setGetter(layerRenderer);
                Undo.RegisterCompleteObjectUndo(map.Map, name);
                Undo.RegisterCompleteObjectUndo(map.Persistent, name);
                Graphics.Blit(clearTexture, map.Map);
                map.UpdatePersistentWithCurrent(parentTerrain.terrainData);
                layerRenderer.SetDirty();
            }
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }
    }
}