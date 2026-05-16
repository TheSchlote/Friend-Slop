using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Microdetail
{
    [Flags]
    public enum LayerMapFlags
    {
        None = 0,
        EmptyDensity = 1,
        EmptyTint = 2
    }
    
    [Serializable]
    public class LayerRenderer : IDisposable
    {
        private static readonly int MicrodetailControlTextureSize = Shader.PropertyToID("MicrodetailControlTextureSize");
        private static readonly int StartIndex = Shader.PropertyToID("StartIndex");
        private static readonly int SamplesPerChunk = Shader.PropertyToID("SamplesPerChunk");
        
        private static readonly int Transformations = Shader.PropertyToID("Transformations");
        private static readonly int SurfaceProperties = Shader.PropertyToID("SurfaceProperties");
        
        private static readonly int Index = Shader.PropertyToID("Index");
        private static readonly int Statistics = Shader.PropertyToID("Statistics");
        
        private static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");
        private static readonly int SplatTexture = Shader.PropertyToID("SplatTexture");
        private static readonly int TerrainPosition = Shader.PropertyToID("TerrainPosition");
        private static readonly int TerrainSize = Shader.PropertyToID("TerrainSize");
        private static readonly int RotationRanges = Shader.PropertyToID("RotationRanges");
        private static readonly int HeightMap = Shader.PropertyToID("HeightMap");
        private static readonly int TintMap = Shader.PropertyToID("TintMap");
        private static readonly int SizeMap = Shader.PropertyToID("SizeMap");
        private static readonly int TerrainNormalAlignFactor = Shader.PropertyToID("TerrainNormalAlignFactor");
        private static readonly int Seed = Shader.PropertyToID("Seed");
        private static readonly int NormalizedSize = Shader.PropertyToID("NormalizedSize");
        private static readonly int CenterShift = Shader.PropertyToID("CenterShift");
        private static readonly int TimeHash = Shader.PropertyToID("Time");
        
        private static readonly int MaximumItemsCount = Shader.PropertyToID("MaximumItemsCount");
        
        private static readonly int HeightmapSize = Shader.PropertyToID("HeightmapSize");
        private static readonly int ViewInfo = Shader.PropertyToID("ViewInfo");
        private static readonly int MicrodetailResolution = Shader.PropertyToID("MicrodetailResolution");
        private static readonly int TintOverSize = Shader.PropertyToID("TintOverSize");
        private static readonly int FadeOverDistance = Shader.PropertyToID("FadeOverDistance");
        private static readonly int MaxRenderDistance = Shader.PropertyToID("MaxRenderDistance");
        private static readonly int RandomTint = Shader.PropertyToID("RandomTint");
        private static readonly int NormalShift = Shader.PropertyToID("NormalShift");
        private static readonly int FlipChance = Shader.PropertyToID("FlipChance");
        private static readonly int Strength = Shader.PropertyToID("_Strength");
        private static readonly int Mask = Shader.PropertyToID("_Mask");
        private static readonly int MaskTexture = Shader.PropertyToID("_MaskTexture");
        private static readonly int MaskTextureScale = Shader.PropertyToID("_MaskTextureScale");
        private static readonly int Threshold = Shader.PropertyToID("_Threshold");

        [SerializeField] private DensityMapSet densityMapSet;
        [SerializeField] private TintMapSet tintMapSet;
        [SerializeField] private string guid;
        [SerializeField] private Layer layer;

        private uint fullRenderedCount;
        private uint fullMaxRenderedCount;

        private ObjectPool<MaterialPropertyBlock> propertyBlockPool = null;
        
#if UNITY_EDITOR
        private bool isDirty = false;
#endif

        private Material brushStampMaterial = null;

        private Material BrushStampMaterial
        {
            get
            {
                if (brushStampMaterial != null)
                    return brushStampMaterial;

                brushStampMaterial = new Material(Resources.Load<Shader>("Microdetail/Shaders/BrushStamp"));

                return brushStampMaterial;
            }
        }
        
        private Material brushEraserMaterial = null;

        private Material BrushEraserMaterial
        {
            get
            {
                if (brushEraserMaterial != null)
                    return brushEraserMaterial;

                brushEraserMaterial = new Material(Resources.Load<Shader>("Microdetail/Shaders/BrushEraser"));

                return brushEraserMaterial;
            }
        }

        private Queue<AsyncGPUReadbackRequest> inProgressRequests = new Queue<AsyncGPUReadbackRequest>();

        public MapSet DensityMapSet => GetMapSet(ref densityMapSet);
        public MapSet TintMapSet => GetMapSet(ref tintMapSet);

        public int MapSize => densityMapSet == null || !densityMapSet.HasPersistent ? 1024 : densityMapSet.Persistent.width;

        public string Guid => guid;

        public Layer Layer => layer;

        private int? readbackCounter = null;

        public LayerMapFlags MapFlags { get; set; } = LayerMapFlags.None;
        
        public LayerRenderer(Layer layer, string guid)
        {
            this.guid = guid;
            this.layer = layer;
        }

        private MapSet GetMapSet<T>(ref T mapSet) where T : MapSet
        {
            mapSet ??= Activator.CreateInstance<T>(); 
            if (!mapSet.IsValid)
                mapSet.SetParent(this);
            
            return mapSet;
        }

        private Texture2D GetPersistentMap(ref Texture2D current, GraphicsFormat format, string name)
        {
            if (current != null)
                return current;
            
            current = new Texture2D(MapSize, MapSize, format, 0, TextureCreationFlags.None);
            current.name = string.Format(name, Guid);
            return current;
        }

        public void UpdateWithPersistentMaps()
        {
            DensityMapSet.UpdateFromPersistent();
            TintMapSet.UpdateFromPersistent();
            
            LayerCollector.RegisterRendererToCheck(this);
        }
        
#if UNITY_EDITOR
        public void UpdatePersistentWithCurrent(TerrainData terrainData)
        {
            if (!isDirty)
                return;

            isDirty = false;
            
            DensityMapSet.UpdatePersistentWithCurrent(terrainData);
            TintMapSet.UpdatePersistentWithCurrent(terrainData);
        }

        public void SaveDirty()
        {
            DensityMapSet.SaveIfDirty();
            TintMapSet.SaveIfDirty();
        }

        public void SetDirty()
        {
            isDirty = true;
            LayerCollector.RegisterRendererToCheck(this);
        }
        
        public void Resize(TerrainData data, int resolution)
        {
            DensityMapSet.Resize(data, resolution);
            TintMapSet.Resize(data, resolution);
            SetDirty();
            UpdatePersistentWithCurrent(data);
            LayerCollector.RegisterRendererToCheck(this);
        }
#endif
        
        public static Bounds TransformBoundsToTerrainSpace(Bounds bounds, Terrain terrain)
        {
            var terrainPosition = terrain.transform.position;
            var terrainSize = terrain.terrainData.size;

            var localCenter = bounds.center - terrainPosition;

            localCenter.x /= terrainSize.x;
            localCenter.y /= terrainSize.y;
            localCenter.z /= terrainSize.z;

            var localSize = new Vector3(
                    bounds.size.x / terrainSize.x,
                    bounds.size.y / terrainSize.y,
                    bounds.size.z / terrainSize.z
                );

            return new Bounds(localCenter, localSize);
        }

        private void UpdateMapSet(MapSet set, Terrain terrain, BrushMode targetBrushMode)
        {
            var brushes = ListPool<Brush>.Get();

            try
            {
                BrushRegistry.GetBrushes(brushes);
                
                var terrainPosition = new Vector2(terrain.transform.position.x, terrain.transform.position.z);
                var terrainSize = terrain.terrainData.size;
                
                var previousRenderTexture = RenderTexture.active;
                RenderTexture.active = set.ProceduralMap;

                GL.PushMatrix();
                GL.LoadOrtho();

                foreach (var brush in brushes)
                {
                    if (brush.Layer != layer)
                        continue;
                    
                    if (brush.Mode != targetBrushMode)
                        continue;

                    var mask = Vector4.zero;
                    for (var index = 0; index < 4; index++)
                        mask[index] = ((int)brush.Mask & (1 << index)) == 0 ? 0 : 1;

                    BrushStampMaterial.SetTexture(MaskTexture, brush.MaskTexture);
                    BrushStampMaterial.SetFloat(MaskTextureScale, brush.MaskTextureScale);
                    BrushStampMaterial.SetVector(Mask, mask);
                    BrushStampMaterial.SetFloat(Threshold,  brush.Threshold);
                    switch (targetBrushMode)
                    {
                        case BrushMode.Stamp:
                            BrushStampMaterial.mainTexture = brush.Texture;
                            BrushStampMaterial.SetFloat(Strength, brush.Strength);
                            BrushStampMaterial.SetPass(0);
                            break;
                        case BrushMode.Eraser:
                            BrushEraserMaterial.mainTexture = brush.Texture;
                            BrushEraserMaterial.SetFloat(Strength, brush.Strength);
                            BrushEraserMaterial.SetPass(0);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(targetBrushMode), targetBrushMode, null);
                    }
                    
                    var halfSize = brush.Size * 0.5f;
                    halfSize.x /= terrainSize.x;
                    halfSize.y /= terrainSize.z;

                    var center = brush.Position - terrainPosition;
                    center.x /= terrainSize.x;
                    center.y /= terrainSize.z;
                    
                    var angle = brush.Angle;
                    var cos = Mathf.Cos(-angle);
                    var sin = Mathf.Sin(-angle);

                    float x, y, rx, ry;

                    GL.Begin(GL.QUADS);
                    x = -halfSize.x; 
                    y = -halfSize.y;
                    rx = cos * x - sin * y; 
                    ry = sin * x + cos * y;
                    GL.TexCoord2(0.0f, 0.0f); 
                    GL.Vertex3(center.x + rx, center.y + ry, 0.0f);

                    x = halfSize.x; 
                    y = -halfSize.y;
                    rx = cos * x - sin * y; 
                    ry = sin * x + cos * y;
                    GL.TexCoord2(1.0f, 0.0f); 
                    GL.Vertex3(center.x + rx, center.y + ry, 0.0f);

                    x = halfSize.x; 
                    y = halfSize.y;
                    rx = cos * x - sin * y; 
                    ry = sin * x + cos * y;
                    GL.TexCoord2(1.0f, 1.0f); 
                    GL.Vertex3(center.x + rx, center.y + ry, 0.0f);

                    x = -halfSize.x; 
                    y = halfSize.y;
                    rx = cos * x - sin * y; 
                    ry = sin * x + cos * y;
                    GL.TexCoord2(0.0f, 1.0f); 
                    GL.Vertex3(center.x + rx, center.y + ry, 0.0f);
                    
                    GL.End();
                }

                GL.PopMatrix();
                RenderTexture.active = previousRenderTexture;
            }
            finally
            {
                ListPool<Brush>.Release(brushes);
            }
        }

        public void UpdateMaps(Terrain terrain)
        {
            var mapSet = GetMapSet(ref densityMapSet);
            var tintSet = GetMapSet(ref tintMapSet);
            if (!UpdateProceduralMap(mapSet))
                return;
            
            tintMapSet.EnsureHasMap();
            mapSet.EnsureHasMap();
            
            UpdateMapSet(mapSet, terrain, BrushMode.Stamp);
            UpdateMapSet(mapSet, terrain, BrushMode.Eraser);
        }

        private bool UpdateProceduralMap(MapSet set)
        {
            var brushes = ListPool<Brush>.Get();

            try
            {
                BrushRegistry.GetBrushes(brushes);
                var brushesCount = 0;
                foreach (var brush in brushes)
                {
                    if (brush.Layer != layer)
                        continue;

                    brushesCount++;
                }

                if (brushesCount == 0)
                {
                    set.ClearProceduralMap();
                    return false;
                }

                Graphics.Blit(set.Map, set.ProceduralMap);
            }
            finally
            {
                ListPool<Brush>.Release(brushes);
            }

            return true;
        }

        private bool ShallBeRendered
        {
            get
            {
                if (DensityMapSet.PendingPreview)
                    return true;

                if (DensityMapSet.HasProcedural)
                    return true;
                
                return (MapFlags & LayerMapFlags.EmptyDensity) == LayerMapFlags.None;
            }
        }

        public void Render(Terrain terrain, Camera camera, ResourcesAllocator fullAllocator, TerrainGraphicalInfo terrainGraphicalInfo, bool isTemporary)
        {
            if (!ShallBeRendered)
                return;
            
            if (layer == null)
                return;

            inProgressRequests ??= new Queue<AsyncGPUReadbackRequest>();

            while (inProgressRequests.Count > 0)
            {
                var top = inProgressRequests.Peek();
                if (!top.done && !top.hasError)
                    break;

                inProgressRequests.Dequeue();
                if (top.hasError)
                    break;
                
                ProcessReadback(top);
            }

#if MICRODETAIL_PROFILING
            Profiler.BeginSample("Render preparation");
#endif
            
            var splatToUse = DensityMapSet.TextureToDraw;
            var tintToUse = TintMapSet.TextureToDraw;

            var totalCount = 0u;
            var totalBufferSize = 0u;
            var layerScaler = Settings.GetLayerEntriesPerUnitAreaScaler(layer);

            propertyBlockPool ??= new ObjectPool<MaterialPropertyBlock>(x => x.Clear(), x => x.Clear(), true);

            var allocatedPropertyBlocks = ListPool<MaterialPropertyBlock>.Get();
            
#if MICRODETAIL_PROFILING
            Profiler.EndSample();
#endif
            
            foreach (var layerEntry in layer.Entries)
            {
                if (layerEntry == null)
                    continue;
   
                var asset = layerEntry.Asset;
                if (asset == null)
                    continue;
                
                if (!asset.Enabled)
                    continue;
                
                if (asset.Material == null)
                    continue;
                
#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Setting up shaders");
#endif
                
                asset.Material.DisableKeyword("MICRODETAIL_PREVIEW");

                var sizeData = asset.UniformSize.Calculate();
                
                var drawingDistance = layerEntry.Asset.DrawingDistance;
                var texelSize = new float2(terrain.terrainData.size.x, terrain.terrainData.size.z) / new float2(splatToUse.width, splatToUse.height);
                var cameraInfo = new CameraInfo(camera, (math.max(texelSize.x, texelSize.y) + sizeData.Max) * 1.45f, drawingDistance);

                var bounds = cameraInfo.ViewInfo.GetViewInfo();
                var terrainBounds = TransformBoundsToTerrainSpace(bounds, terrain);
                 
                var samplesCount = layerEntry.SamplesPerUnitArea * layerScaler;
                var textureArea = splatToUse.width * splatToUse.height;
                var terrainArea = terrain.terrainData.size.x * terrain.terrainData.size.z;
                var ratio = terrainArea / textureArea;
                var samplesPerUnitArea = samplesCount * ratio;
                
                var flatMax = new float2(terrainBounds.max.x, terrainBounds.max.z);
                flatMax *= MapSize;
                
                var flatMin = new float2(terrainBounds.min.x, terrainBounds.min.z);
                flatMin *= MapSize;

                flatMax = math.clamp(flatMax, 0, MapSize);
                flatMin = math.clamp(flatMin, 0, MapSize);
                
                var startingPosition = (int2)math.floor(flatMin);
                var fragmentsToProcess = (int2)math.ceil(flatMax) - startingPosition;
                
                var count = (uint)math.ceil(Math.Min(Math.Max(10000, fullMaxRenderedCount * 1.5), 10000000));
                var resources = fullAllocator.GetResources(layerEntry, count);

                totalBufferSize += resources.MemorySize;

                MeshUtility.GenerateParallelepiped(resources.Mesh, asset.AspectRatio);

#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Updating buffer");
#endif
                if (DebugProperties.UpdateBuffers)
                    resources.Reset(resources.Mesh.GetIndexCount(0));
                
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif

#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Updating frustum");
#endif
                var frustumSet = new NativeArray<ViewParameters>(1, Allocator.Temp);
                frustumSet[0] = cameraInfo.ViewInfo;
                resources.CameraBuffer.SetData(frustumSet);
                frustumSet.Dispose();
                
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif

#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Setting up compute shader");
#endif
                
                var shader = resources.PopulateMicrodetailsShader;
                var kernel = resources.PopulateMicrodetailsShaderKernel;

                var terrainSize = terrain.terrainData.size;
        
#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Applying default modules");
#endif
                
                BuiltInModuleUtility.ApplyDefaultModules(kernel, shader);
                asset.SetupDefaultPopulateShader(kernel, shader);
        
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif
                if ((MapFlags & LayerMapFlags.EmptyTint) == LayerMapFlags.None)
                    shader.DisableKeyword("MICRODETAIL_TINT");
                else
                    shader.EnableKeyword("MICRODETAIL_TINT");
                    
                shader.SetInt(MaximumItemsCount, (int)resources.PropsCount);
                
                shader.SetVector(CenterShift, (Vector3)asset.CenterShift);
                shader.SetVector(TerrainPosition, terrain.transform.position);
                shader.SetVector(Seed, (Vector2)layerEntry.Seed);
                shader.SetVector(RotationRanges, new Vector4(
                    asset.PlanarRotationRange.Min, asset.PlanarRotationRange.Max,
                    asset.NormalRotationRange.Min, asset.NormalRotationRange.Max) * Mathf.Deg2Rad);
                
                shader.SetInt(HeightmapSize, terrain.terrainData.heightmapResolution);
                shader.SetVector(NormalShift, new Vector4(asset.NormalShift.Min, asset.NormalShift.Max));
                
                shader.SetVector(TerrainSize,
                    new Vector3(terrainSize.x, terrain.terrainData.heightmapScale.y * 2.0f, terrainSize.z));
                
                shader.SetVector(NormalizedSize, (Vector3)asset.AspectRatio);
                
                if (asset is SDFAsset sdfAsset)
                    shader.SetVector(MicrodetailResolution, new Vector3(sdfAsset.SDF.width, sdfAsset.SDF.height, sdfAsset.SDF.depth));
                
                #if UNITY_EDITOR
                    if (Application.isPlaying)
                        shader.SetFloat(TimeHash, Time.time);
                    else
                        shader.SetFloat(TimeHash, (float)UnityEditor.EditorApplication.timeSinceStartup);
                #else
                    shader.SetFloat(TimeHash, Time.time);
                #endif
                
                shader.SetFloat(TerrainNormalAlignFactor, asset.TerrainAlignFactor);
                shader.SetFloat(SamplesPerChunk, samplesPerUnitArea);
                shader.SetFloat(FlipChance, asset.FlipChance);
                shader.SetInt2(MicrodetailControlTextureSize, new int2(splatToUse.width, splatToUse.height));
                shader.SetInt2(StartIndex, new int2(startingPosition.x, startingPosition.y));
                shader.SetFloat(MaxRenderDistance, drawingDistance);
                shader.SetBuffer(kernel, ViewInfo, resources.CameraBuffer);
                shader.SetTexture(kernel, SplatTexture, splatToUse);
                shader.SetTexture(kernel, HeightMap, terrain.terrainData.heightmapTexture);
                shader.SetTexture(kernel, TintMap, tintToUse);
                
                shader.SetTexture(kernel, TintOverSize, asset.TintOverSize.GradientTexture);
                shader.SetTexture(kernel, FadeOverDistance, asset.FadeOverDistance.Calculate().Texture);
                shader.SetTexture(kernel, SizeMap, sizeData.Texture);
                shader.SetTexture(kernel, RandomTint, asset.RandomTint.GradientTexture);
                
                shader.SetBuffer(kernel, Transformations, resources.TransformationBuffer);
                shader.SetBuffer(kernel, SurfaceProperties, resources.MicrodetailPropertiesBuffer);
                shader.SetBuffer(kernel, Statistics, resources.StatisticBuffer);
                shader.SetBuffer(kernel, Index, resources.IndirectArgumentsBuffer);

                var groupsCount = new int3(fragmentsToProcess.x, 1, fragmentsToProcess.y);
                var numThreads = new uint3();
                shader.GetKernelThreadGroupSizes(kernel, out numThreads.x, out numThreads.y, out numThreads.z);
                groupsCount = (int3)math.ceil((float3)groupsCount / (int3)numThreads);
                
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif
                
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif
                
                if (math.any(groupsCount == 0))
                    continue;

#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Dispatching compute shader");
#endif
                
                if (DebugProperties.UpdateBuffers)
                    shader.Dispatch(kernel, groupsCount.x, groupsCount.y, groupsCount.z);
                
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif
                
                if (layer.Visible && DebugProperties.RenderFull)
                {
#if MICRODETAIL_PROFILING
                    Profiler.BeginSample("Setting up rendering");
#endif
                    
                    var propertyBlock = propertyBlockPool.Get();
                    allocatedPropertyBlocks.Add(propertyBlock);
                    var renderingParameters = new RenderParams(null)
                    {
                        layer = layerEntry.Asset.Layer,
                        renderingLayerMask = layerEntry.Asset.RenderingLayerMask,
                        receiveShadows = layerEntry.Asset.CastShadows,
                        shadowCastingMode = ShadowCastingMode.On,
                        lightProbeUsage = LightProbeUsage.UseProxyVolume,
                        reflectionProbeUsage = layerEntry.Asset.ReflectionProbeUsage,
                        worldBounds = new Bounds(Vector3.zero, 1000000.0f * Vector3.one),
                        matProps = propertyBlock
                    }; 
                    
                    SetupParameters(terrain, camera, terrainGraphicalInfo, asset, asset.Material, ref renderingParameters, resources, terrainSize);
                    Graphics.RenderMeshIndirect(renderingParameters, resources.Mesh, resources.IndirectArgumentsBuffer);
                    
#if MICRODETAIL_PROFILING
                    Profiler.EndSample();
#endif
                }

#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Clearing");
#endif
                TintMapSet.ClearPreview();
                DensityMapSet.ClearPreview();
                
#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif

#if MICRODETAIL_PROFILING
                Profiler.BeginSample("Requesting GPU info readback");
#endif

                if (DebugProperties.GetReadback)
                {
                    readbackCounter ??= Random.Range(0, 30);

                    if (readbackCounter-- <= 0)
                    {
                        readbackCounter = 30;
                        inProgressRequests.Enqueue(AsyncGPUReadback.Request(resources.StatisticBuffer));
                    }
                }

#if MICRODETAIL_PROFILING
                Profiler.EndSample();
#endif

                totalCount += fullRenderedCount;
            }

#if MICRODETAIL_PROFILING
            Profiler.BeginSample("Releasing resources");
#endif
            
            foreach (var allocatedBlock in allocatedPropertyBlocks)
                propertyBlockPool.Release(allocatedBlock);
            
            ListPool<MaterialPropertyBlock>.Release(allocatedPropertyBlocks);
            
#if MICRODETAIL_PROFILING
            Profiler.EndSample();
#endif
            
            if (!DebugProperties.CollectData)
                return;

#if MICRODETAIL_PROFILING
            Profiler.BeginSample("Collecting samples");
#endif
            
            DebugProperties.LayersData[new DebugPropertyLayerDescriptor(layer, isTemporary)] = new LayerStatisticData
                {
                    EntriesCount = totalCount,
                    BufferSize = totalBufferSize,
                    ExpectedCount = fullMaxRenderedCount
                };
            
#if MICRODETAIL_PROFILING
            Profiler.EndSample();
#endif
        }

        private static void SetupParameters(Terrain terrain, Camera camera, TerrainGraphicalInfo graphicalInfo,
            MicrodetailAsset asset, Material material, ref RenderParams parameters, DrawingResources resources, Vector3 terrainSize)
        {
            BuiltInModuleUtility.ApplyDefaultModules(material, parameters.matProps, graphicalInfo);

            parameters.camera = camera;
            parameters.material = material;
            parameters.shadowCastingMode =
                asset.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                
            parameters.matProps.SetMatrix(ObjectToWorld, Matrix4x4.identity);
            parameters.matProps.SetBuffer(MicrodetailMaterialProperties.Transformation, resources.TransformationBuffer);
            parameters.matProps.SetBuffer(MicrodetailMaterialProperties.Properties, resources.MicrodetailPropertiesBuffer);
                
            if (graphicalInfo.Diffuse != null)
                parameters.matProps.SetTexture(MicrodetailMaterialProperties.Textures, graphicalInfo.Diffuse);
                
            if (graphicalInfo.Alphamap != null)
                parameters.matProps.SetTexture(MicrodetailMaterialProperties.SplatMaps, graphicalInfo.Alphamap);
                
            if (graphicalInfo.Normal != null)
                parameters.matProps.SetTexture(MicrodetailMaterialProperties.NormalMaps, graphicalInfo.Normal);
                
            if (graphicalInfo.Mask != null)
                parameters.matProps.SetTexture(MicrodetailMaterialProperties.MaskMaps, graphicalInfo.Mask);
                
            parameters.matProps.SetVector(MicrodetailMaterialProperties.TerrainSize, new Vector3(terrainSize.x, terrain.terrainData.heightmapScale.y * 2.0f, terrainSize.z));
            parameters.matProps.SetVector(MicrodetailMaterialProperties.TerrainPosition, terrain.transform.position);
            parameters.matProps.SetTexture(MicrodetailMaterialProperties.HeightMap, terrain.terrainData.heightmapTexture);
            parameters.matProps.SetVector(MicrodetailMaterialProperties.HeightMapSize, new Vector4(terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution));
            parameters.matProps.SetInt(MicrodetailMaterialProperties.LayersCount, graphicalInfo.Diffuse == null ? 0 : graphicalInfo.Diffuse.depth);
            parameters.matProps.SetInt(MicrodetailMaterialProperties.SplatMapsCount, graphicalInfo.Alphamap == null ? 0 : graphicalInfo.Alphamap.volumeDepth);
            parameters.matProps.SetVector(MicrodetailMaterialProperties.NormalizedSize, (Vector3)asset.AspectRatio);
            if (graphicalInfo.Positionings != null)
                parameters.matProps.SetVectorArray(MicrodetailMaterialProperties.SplatMapPositioning, graphicalInfo.Positionings);
                
            if (graphicalInfo.MaskRemapMinimum != null)
                parameters.matProps.SetVectorArray(MicrodetailMaterialProperties.MinRemap, graphicalInfo.MaskRemapMinimum);
                
            if (graphicalInfo.MaskRemapMinimum != null)
                parameters.matProps.SetVectorArray(MicrodetailMaterialProperties.MaxRemap, graphicalInfo.MaskRemapMaximum);
                
            if (graphicalInfo.DefaultValues != null)
                parameters.matProps.SetVectorArray(MicrodetailMaterialProperties.BlendDefaults, graphicalInfo.DefaultValues);

            if (graphicalInfo.Alphamap != null)
            {
                parameters.matProps.SetVector(MicrodetailMaterialProperties.SplatmapSize,
                    new Vector4(1.0f / graphicalInfo.Alphamap.width,
                        1.0f / graphicalInfo.Alphamap.height,
                        graphicalInfo.Alphamap.width, graphicalInfo.Alphamap.height));
            }

#if MICRODETAIL_PROFILING
            Profiler.BeginSample("Preparing asset for rendering");
#endif
            asset.PrepareForRendering(material, parameters.matProps, graphicalInfo);
            
#if MICRODETAIL_PROFILING
            Profiler.EndSample();
#endif
        }

        private void ProcessReadback(AsyncGPUReadbackRequest request)
        {
            var statistic = request.GetData<StatisticInfo>(0);
            fullRenderedCount = statistic[0].DesiredCount;
            fullMaxRenderedCount = Math.Max(fullMaxRenderedCount, fullRenderedCount);
        }

        public void Dispose()
        {
            DensityMapSet?.Dispose();
            TintMapSet?.Dispose();
        }
    }
}