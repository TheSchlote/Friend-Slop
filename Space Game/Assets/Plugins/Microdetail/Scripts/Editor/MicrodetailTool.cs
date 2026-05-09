using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TerrainTools;

namespace Microdetail
{
    public abstract class MicrodetailTool<T> : TerrainToolsPaintTool<T> where T : TerrainToolsPaintTool<T>
    {
        private static readonly int BrushTex = Shader.PropertyToID("_BrushTex");
        private static readonly int BrushParams = Shader.PropertyToID("_BrushParams");

        private static Material previewMaterial;

        private static Material PreviewMaterial
        {
            get
            {
                if (previewMaterial != null)
                    return previewMaterial;
                
                previewMaterial = new Material(Resources.Load<Shader>("Microdetail/Shaders/BrushPreview"));

                return previewMaterial;
            }
        }

        private static PaletteEditor paletteEditor;

        private static Palette palette;
        protected abstract Material Material { get; }
        
        protected string PluginPath => "Assets/Plugins/Microdetail/";
        
        protected abstract string ClearName { get; }

        private static List<Palette> AllPalettes
        {
            get
            {
                var entries = AssetDatabase.FindAssets("t:Palette");
                var paths = Array.ConvertAll(entries, AssetDatabase.GUIDToAssetPath);
                var assets = Array.ConvertAll(paths, AssetDatabase.LoadAssetAtPath<Palette>).ToList();
                assets.RemoveAll(x => x == null);

                return assets;
            }
        }

        protected static Palette Palette
        {
            get
            {
                if (palette != null)
                    return palette;

                palette = AllPalettes[0];
                return palette;
            }
        }

        public override TerrainCategory Category => TerrainCategory.Foliage;

        public override bool HasToolSettings => true;
    
        public override bool HasBrushAttributes => true;

        private IBrushUIGroup CommonUI 
        {
            get
            {
                if (m_commonUI != null) 
                    return m_commonUI;
                
                m_commonUI = new BrushUIGroup(GetName());
                m_commonUI.OnEnterToolMode();

                return m_commonUI;
            }
        }

        protected abstract MapSet GetMapSet(LayerRenderer renderer);
        
        public override void OnInspectorGUI(Terrain terrain, IOnInspectorGUI editContext)
        {
            CommonUI.OnInspectorGUI(terrain, editContext, BrushGUIEditFlags.All);

            DrawSettings(terrain);
            
            EditorGUILayout.Space(30);

            if (GUILayout.Button("Open converter tool"))
                PropsConverterWindow.Open();

            var paletteName = Palette == null ? "none" : Palette.name;
            if (EditorGUILayout.DropdownButton(new GUIContent($"Selected palette: {paletteName}"),
                    FocusType.Keyboard))
            {
                var palettes = AllPalettes;
                var menu = new GenericMenu();

                foreach (var paletteToAdd in palettes)
                {
                    var cachedEntry = paletteToAdd;
                    
                    menu.AddItem(new GUIContent(paletteToAdd.name), cachedEntry == Palette, () => palette = cachedEntry);
                }
                
                menu.ShowAsContext();
            }
            
            if (Palette != null)
            {
                Editor result = paletteEditor;
                Editor.CreateCachedEditor(Palette, typeof(PaletteEditor), ref result);
                paletteEditor = result as PaletteEditor;

                if (paletteEditor != null)
                {
                    paletteEditor.Context = terrain;
                    paletteEditor.OnInspectorGUI();
                }
            }

            DrawInfo(terrain);
        }

        private void DrawInfo(Terrain terrain)
        {
            EditorGUILayout.LabelField("Layer details", EditorStyles.boldLabel);
            
            if (Palette == null)
            {
                EditorGUILayout.HelpBox("No palette was selected.", MessageType.Info);
                return;
            }

            var renderer = RendererUtility.GetMicrodetailRenderer(terrain);
            if (renderer == null)
            {
                EditorGUILayout.HelpBox("No rendering was done on the terrain.", MessageType.Info);
                return;
            }

            var layer = renderer.HasLayer(Palette.SelectedLayer) ? renderer.GetLayer(Palette.SelectedLayer) : null;
            if (layer == null)
                return;
            
            EditorGUILayout.LabelField($"Layer with id {layer.Guid}");
            if (GUILayout.Button(ClearName))
            {
                Clear();
                BrushRegistry.MarkLayerDirty(layer.Layer);
            }

            GUILayout.BeginHorizontal();
            
            GUILayout.Label("Layer textures size");

            if (EditorGUILayout.DropdownButton(new GUIContent($"{layer.MapSize}x{layer.MapSize}"), FocusType.Keyboard))
            {
                var menu = new GenericMenu();

                var resolutions = new List<int>()
                    {
                        1024,
                        2048,
                        4096
                    };

                foreach (var resolution in resolutions)
                {
                    var cachedResolution = resolution;
                    menu.AddItem(new GUIContent($"{resolution}x{resolution}"), resolution == layer.MapSize, () =>
                        {
                            if (EditorUtility.DisplayDialog("Warning", "Changing the resolution is irreversible. Are you sure you want to proceed?", "Resize", "Keep as it is"))
                                layer.Resize(terrain.terrainData, cachedResolution);
                        });
                }

                menu.ShowAsContext();
            }
            
            GUILayout.EndHorizontal();

#if DRAW_ADDITIONAL_MICRODETAIL_INFO
            DrawMapSet(layer.DensityMapSet, "Density Map");
            DrawMapSet(layer.TintMapSet, "Tint Map");
#endif
        }

        protected virtual void DrawSettings(Terrain terrain)
        {
            
        }

#if DRAW_ADDITIONAL_MICRODETAIL_INFO
        private void DrawMapSet(MapSet mapSet, string mapName)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            var size = 200;
            EditorGUILayout.LabelField($"{mapName} Map/Persistent", GUILayout.Width(100));
            GUILayout.Box(mapSet.Map, GUILayout.Width(size), GUILayout.Height(size));
            GUILayout.Box(mapSet.Persistent, GUILayout.Width(size), GUILayout.Height(size));
            EditorGUILayout.EndHorizontal();
        }
#endif

        protected abstract void Clear();
        
        public override void OnEnterToolMode()
        {
            CommonUI.OnEnterToolMode();
        }

        public override void OnExitToolMode()
        {
            CommonUI.OnExitToolMode();
        }

        private LayerRenderer GetRenderer(Terrain terrain, Layer layer)
        {
            var renderer = RendererUtility.GetMicrodetailRenderer(terrain);
            return renderer.HasLayer(layer) ? renderer.GetLayer(layer) : renderer.GetTemporaryLayer(layer);
        }

        private void RenderIntoPaintContext(PaintContext paintContext, Texture brushTexture, BrushTransform brushXform)
        {
            var brushMask = RTUtils.GetTempHandle(paintContext.sourceRenderTexture.width, paintContext.sourceRenderTexture.height, 0, UnityEditor.TerrainTools.FilterUtility.defaultFormat);
            Utility.GenerateAndSetFilterRT(CommonUI, paintContext.sourceRenderTexture, brushMask, Material);
            
            Material.SetTexture(BrushTex, brushTexture);
            var opacity = (Event.current.control ? -1.0f : 1.0f) * CommonUI.brushStrength;
            Material.SetVector(BrushParams, new Vector4(opacity, 0.0f, 0.0f, 0.0f));
            TerrainPaintUtility.SetupTerrainToolMaterialProperties(paintContext, brushXform, Material);
            
            Graphics.Blit(paintContext.sourceRenderTexture, paintContext.destinationRenderTexture, Material, 0);
            
            RTUtils.Release(brushMask);
        }

        private List<LayerRenderer> GetAffectedLayers(Terrain terrain)
        {
            var renderer = RendererUtility.GetMicrodetailRenderer(terrain);
            if (Event.current.alt && Event.current.control)
                return renderer.AllLayers;
            
            if (Palette == null)
                return new List<LayerRenderer>();

            if (Palette.LayersCount == 0 || Palette.SelectedIndex == -1)
                return new List<LayerRenderer>();
            
            if (Palette.SelectedLayer == null)
                return new List<LayerRenderer>();

            return new List<LayerRenderer> { renderer.HasLayer(Palette.SelectedLayer) ? 
                renderer.GetLayer(Palette.SelectedLayer) : 
                renderer.GetTemporaryLayer(Palette.SelectedLayer) };
        }

        public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (!editContext.hitValidTerrain)
                return;
            
            var layers = GetAffectedLayers(terrain);
            
            DrawTexturePreview(terrain, editContext.brushTexture);

            foreach (var layer in layers)
            {
                Paint(layer, terrain,
                    new BrushParameters(editContext.raycastHit.textureCoord, editContext.brushTexture),
                    (layerToGather, _) =>
                    {
                        var set = GetMapSet(layerToGather);
                        Graphics.Blit(set.TargetTexture, set.Preview);
                        return set.Preview;
                    },
                    terrainInfo => GetMapSet(GetRenderer(terrainInfo.terrain, layer.Layer)).MarkToRenderPreview());
            }
        }

        private void ApplyBrushScatter(ref Terrain terrain, ref Vector2 uv)
        {
            var originalValue = CommonUI.brushScatter;
            CommonUI.brushScatter *= 0.001f;
            CommonUI.ScatterBrushStamp(ref terrain, ref uv);
            CommonUI.brushScatter = originalValue;
        }

        private void DrawTexturePreview(Terrain terrain, Texture texture)
        {
            var worldRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            if (!terrain.GetComponent<Collider>().Raycast(worldRay, out var hitInfo, float.PositiveInfinity))
                return;
            
            var uv = (Vector2)hitInfo.textureCoord;
            var terrainToPaintOn = terrain;
            ApplyBrushScatter(ref terrainToPaintOn, ref uv);

            var brushTransform = TerrainPaintUtility.CalculateBrushTransform(terrainToPaintOn, uv, CommonUI.brushSize, CommonUI.brushRotation);
            var paintContext = TerrainPaintUtility.BeginPaintHeightmap(terrainToPaintOn, brushTransform.GetBrushXYBounds(), 1);
            TerrainPaintUtilityEditor.DrawBrushPreview(paintContext, TerrainBrushPreviewMode.SourceRenderTexture, texture, brushTransform, PreviewMaterial, 0);
            TerrainPaintUtility.ReleaseContextResources(paintContext);
        }
 
        private void Paint(LayerRenderer layerRenderer, Terrain terrain, BrushParameters parameters, Func<LayerRenderer, Terrain, RenderTexture> textureProvider, Action<PaintContext.ITerrainInfo> beforeBlit = null)
        {
            var uv = (Vector2)parameters.UV;
            ApplyBrushScatter(ref terrain, ref uv);
            
            var brushXform = TerrainPaintUtility.CalculateBrushTransform(terrain, uv,
                CommonUI.brushSize, CommonUI.brushRotation);

            var paintContext = PaintContext.CreateFromBounds(terrain,
                brushXform.GetBrushXYBounds(), layerRenderer.MapSize, layerRenderer.MapSize);

            var mapSet = GetMapSet(layerRenderer);
            paintContext.CreateRenderTargets(GraphicsFormatUtility.GetRenderTextureFormat(mapSet.Format));
            
            paintContext.Gather(terrainInfo => GetMapSet(GetRenderer(terrainInfo.terrain, layerRenderer.Layer)).TargetTexture,
                Color.black, beforeBlit: beforeBlit);
            
            RenderIntoPaintContext(paintContext, parameters.Texture, brushXform);
            
            paintContext.Scatter(terrainInfo => textureProvider(GetRenderer(terrainInfo.terrain, layerRenderer.Layer), terrainInfo.terrain));
            paintContext.Cleanup();
        }

        public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
        {
            CommonUI.OnSceneGUI2D(terrain, editContext);
            base.OnSceneGUI(terrain, editContext);
            var spacingDownscale = 30.0f;
            var originalSpacing = CommonUI.brushSpacing;
            CommonUI.brushSpacing /= spacingDownscale;
            CommonUI.OnSceneGUI(terrain, editContext);
            CommonUI.brushSpacing = originalSpacing;
        }

        public override bool OnPaint(Terrain terrain, IOnPaint editContext)
        {
            CommonUI.OnPaint(terrain, editContext);
            if (!CommonUI.allowPaint)
                return false;

            var microdetailRenderer = RendererUtility.GetMicrodetailRenderer(terrain);
            var layers = GetAffectedLayers(terrain);
            foreach (var layer in layers)
                if (!microdetailRenderer.HasLayer(layer.Layer))
                    microdetailRenderer.PromoteLayerToPersistent(layer.Layer);

            foreach (var layer in layers)
            {
                var renderersToRecord = new List<(LayerRenderer Renderer, Terrain Terrain)>();
                Paint(layer, terrain, new BrushParameters(editContext.uv, editContext.brushTexture),
                    (layerRenderer, layerTerrain) =>
                    {
                        renderersToRecord.Add((layerRenderer, layerTerrain));
                        return GetMapSet(layerRenderer).Map;
                    });

                var currentMaps = renderersToRecord.ConvertAll(x => (UnityEngine.Object)GetMapSet(x.Renderer).Map)
                    .ToArray();
                var maps = renderersToRecord.ConvertAll(x => (UnityEngine.Object)GetMapSet(x.Renderer).Persistent)
                    .ToArray();
                Undo.RegisterCompleteObjectUndo(maps, StringConstants.PaintMicrodetail + " " + layer.Guid);
                Undo.RegisterCompleteObjectUndo(currentMaps, StringConstants.PaintMicrodetail + " " + layer.Guid);
                foreach (var renderer in renderersToRecord)
                {
                    renderer.Renderer.SetDirty();
                    renderer.Renderer.UpdatePersistentWithCurrent(renderer.Terrain.terrainData);
                }
                
                BrushRegistry.MarkLayerDirty(layer.Layer);
            }

            return false;
        }
    }
}