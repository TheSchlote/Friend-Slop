using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Microdetail
{
    public static class MicrodetailLifetimeHandler
    {
        private static int renderDelay = 4;
        private static List<MicrodetailRenderer> renderers = new List<MicrodetailRenderer>();
        
        [RuntimeInitializeOnLoadMethod]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void Initialize()
        {
            RenderPipelineManager.beginContextRendering -= Render;
            RenderPipelineManager.beginContextRendering += Render;
            
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeDomainReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeDomainReload;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
            UnityEditor.Undo.undoRedoEvent -= OnUndoRedo;
            UnityEditor.Undo.undoRedoEvent += OnUndoRedo;
#endif
        }

#if UNITY_EDITOR
        private static void OnBeforeDomainReload()
        {
            foreach (var renderer in renderers)
                renderer.ReleaseResources();
        }
        
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode && 
                state != UnityEditor.PlayModeStateChange.ExitingEditMode) 
                return;
            
            foreach (var renderer in renderers)
            {
                var data = renderer.RendererData;
                if (data == null)
                    continue;

                data.UpdateWithCurrent();
            }
        }
        
        private static void OnUndoRedo(in UnityEditor.UndoRedoInfo undo)
        {
            if (!undo.undoName.StartsWith(StringConstants.PaintMicrodetail) &&
                !undo.undoName.StartsWith(StringConstants.ClearMicrodetail) &&
                !undo.undoName.StartsWith(StringConstants.ResizeMicrodetail))
                return;

            foreach (var renderer in renderers)
            {
                var data = renderer.RendererData;
                if (data == null)
                    continue;

                data.UpdateWithPersistent();
            }
        }
#endif

        public static void RegisterMicrodetailRenderer(MicrodetailRenderer renderer)
        {
            renderers.Add(renderer);
        }

        public static void UnregisterMicrodetailRenderer(MicrodetailRenderer renderer)
        {
            renderers.Remove(renderer);
        }
        
        private static void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            var tempCameras = ListPool<Camera>.Get();
            tempCameras.AddRange(cameras);
            tempCameras.RemoveAll(x =>
                {
                    var settings = x.GetComponent<CameraSettings>();
                    return settings != null && settings.ExcludeFromRendering;
                });
            
            if (cameras.Count == 0)
                return;
                
            if (renderDelay > 0)
            {
                renderDelay--;
                return;
            }
            
            foreach (var renderer in renderers)
                renderer.Render(context, tempCameras);
            
            ListPool<Camera>.Release(tempCameras);
            BrushRegistry.ClearDirty();
        }
    }
    
    [ExecuteAlways]
    [DefaultExecutionOrder(-10000)]
    public class MicrodetailRenderer : MonoBehaviour
    {
        private TerrainGraphicalInfo terrainGraphicalInfo;
        
        [SerializeField] private RendererData rendererData;

        private List<LayerRenderer> temporaryLayers = new List<LayerRenderer>();
        
        private Terrain ParentTerrain => GetComponentInParent<Terrain>();

        private ResourcesAllocator allocator;
        
        public List<LayerRenderer> AllLayers => RendererData.Layers;

        public RendererData RendererData
        {
            get
            {
#if UNITY_EDITOR
                if (rendererData != null)
                    return rendererData;

                var terrainPath = UnityEditor.AssetDatabase.GetAssetPath(ParentTerrain.terrainData);
                rendererData = UnityEditor.AssetDatabase.LoadAssetAtPath<RendererData>(terrainPath);

                if (rendererData != null) 
                    return rendererData;
                
                var terrainData = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
                rendererData = ScriptableObject.CreateInstance<RendererData>();
                UnityEditor.AssetDatabase.AddObjectToAsset(rendererData, terrainData);

                return rendererData;
#else
                return rendererData;
#endif
            }
        }
        
        public void ReloadComputeShader()
        {
            allocator?.ReloadPlacementComputeShader();
        }

        public void RemoveLayer(Layer layer)
        {
            RendererData.RemoveLayer(layer);
        }
        
        public bool HasLayer(Layer layer) => RendererData.GetLayer(layer) != null;

        public LayerRenderer GetTemporaryLayer(Layer layer)
        {
            temporaryLayers ??= new List<LayerRenderer>();

            LayerRenderer foundRenderer = null;
            foreach (var layerToCheck in temporaryLayers)
            {
                if (layerToCheck.Layer == layer) 
                    foundRenderer =  layerToCheck;
            }
            
            if (foundRenderer != null)
            {
                foundRenderer.MapFlags = LayerMapFlags.EmptyDensity | LayerMapFlags.EmptyTint;
                return foundRenderer;
            }

            foundRenderer = new LayerRenderer(layer, Guid.NewGuid().ToString());
            foundRenderer.MapFlags = LayerMapFlags.EmptyDensity | LayerMapFlags.EmptyTint;
            temporaryLayers.Add(foundRenderer);

            return foundRenderer;
        }

        public void PromoteLayerToPersistent(Layer layer)
        {
            var temporary = GetTemporaryLayer(layer);
            temporary.MapFlags = LayerMapFlags.None;
            LayerCollector.RegisterRendererToCheck(temporary);
            RendererData.AddLayer(temporary);
            temporaryLayers.Remove(temporary);
        }

        public LayerRenderer GetLayer(Layer layer)
        {
            return RendererData.GetLayer(layer);
        }

        private void Start()
        {
            RendererData.UpdateWithPersistent();
        }
        
        private void OnEnable()
        {
            RendererData.UpdateWithPersistent();
            
            MicrodetailLifetimeHandler.RegisterMicrodetailRenderer(this);
        }

        public void ReleaseResources()
        {
            allocator?.Dispose();
            allocator = null;
        }

        private void OnDisable()
        {
            MicrodetailLifetimeHandler.UnregisterMicrodetailRenderer(this);
            if (ParentTerrain == null)
                return;
            
#if UNITY_EDITOR
            RendererData.UpdateWithCurrent();
#endif
            
            ReleaseResources();
        }

        private void OnDestroy()
        {
            terrainGraphicalInfo?.Dispose();
            terrainGraphicalInfo = null;
            
            ReleaseResources();
        }

        public void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!enabled)
                return;
            
            if (!Settings.IsEnabled())
                return;

            var terrain = ParentTerrain;
            if (terrain == null)
                return;
            
            if (!terrain.gameObject.activeInHierarchy)
                return;
            
            Profiler.BeginSample("Updating layer collector");
            LayerCollector.Collect();
            Profiler.EndSample();

            Profiler.BeginSample("Microdetail rendering");
            terrainGraphicalInfo ??= new TerrainGraphicalInfo(terrain.terrainData);
            if (!terrainGraphicalInfo.MatchesState(terrain.terrainData))
                terrainGraphicalInfo.Setup(terrain.terrainData);

            allocator ??= new ResourcesAllocator();

            var passedLayers = HashSetPool<Layer>.Get();
            try
            {
                foreach (var layer in RendererData.Layers)
                {
                    if (layer.Layer == null)
                        continue;

                    Profiler.BeginSample("Update maps");
                    passedLayers.Add(layer.Layer);
                    if (BrushRegistry.IsDirty(layer.Layer))
                        layer.UpdateMaps(terrain);
                    
                    Profiler.EndSample();
                    
                    if (!layer.Layer.Enabled)
                        continue;

                    Profiler.BeginSample("Render all cameras");
                    foreach (var targetCamera in cameras)
                        layer.Render(terrain, targetCamera, allocator, terrainGraphicalInfo, false);
                    
                    Profiler.EndSample();
                }
            }
            finally
            {
                Profiler.BeginSample("Getting brushes");
                var brushes = ListPool<Brush>.Get();
                BrushRegistry.GetBrushes(brushes);
                
                Profiler.EndSample();
                
                Profiler.BeginSample("Getting temporary layers");
                foreach (var brush in brushes)
                    if (brush.Layer != null && !passedLayers.Contains(brush.Layer))
                        GetTemporaryLayer(brush.Layer);

                ListPool<Brush>.Release(brushes);
                HashSetPool<Layer>.Release(passedLayers);
                
                Profiler.EndSample();
            }
            
            Profiler.BeginSample("Updating temporary layers");
            foreach (var layer in temporaryLayers)
            {
                if (layer.Layer == null)
                    continue;

                Profiler.BeginSample("Update maps");
                if (BrushRegistry.IsDirty(layer.Layer))
                    layer.UpdateMaps(terrain);
                
                Profiler.EndSample();

                if (!layer.Layer.Enabled)
                    continue;
                
                Profiler.BeginSample("Render all cameras"); 
                foreach (var targetCamera in cameras)
                    layer.Render(terrain, targetCamera, allocator, terrainGraphicalInfo, true);
                
                Profiler.EndSample();
            }
            
            Profiler.EndSample();
            
            Profiler.EndSample();
        }
    }
}