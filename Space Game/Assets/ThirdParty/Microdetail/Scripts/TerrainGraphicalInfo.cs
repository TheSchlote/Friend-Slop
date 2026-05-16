using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Microdetail
{
    public class TerrainGraphicalInfo : IDisposable
    {
        private static Texture2D defaultClearMask;

        private static Texture2D DefaultClearMask
        {
            get
            {
                if (defaultClearMask != null)
                    return defaultClearMask;

                defaultClearMask = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                defaultClearMask.SetPixel(0, 0, new Color(0.0f, 1.0f, 0.0f, 0.0f));
                defaultClearMask.Apply();

                return defaultClearMask;
            }
        }
        
        private Dictionary<int, Texture2D> diffuseTextures = new Dictionary<int, Texture2D>();
        private Dictionary<int, Texture2D> maskMaps = new Dictionary<int, Texture2D>();
        private Dictionary<int, Texture2D>  normalMaps = new Dictionary<int, Texture2D>();
        public Texture2DArray Diffuse { get; private set; }
        public Texture2DArray Mask { get; private set; }
        public Texture2DArray Normal { get; private set; }
        public RenderTexture Alphamap { get; private set; }

        public Vector4[] Positionings { get; set; }
        public Vector4[] MaskRemapMinimum { get; set; }
        public Vector4[] MaskRemapMaximum { get; set; }
        public Vector4[] DefaultValues { get; set; }

        private bool isDirty = false;

        public bool IsEmpty => Diffuse == null || Normal == null || Mask == null || Alphamap == null;

        public TerrainGraphicalInfo(TerrainData terrainData)
        {
            Setup(terrainData);

            TerrainCallbacks.textureChanged += MarkDirty;
        }

        private void MarkDirty(Terrain terrain, string name, RectInt region, bool synched) => isDirty = true;

        private int GetMaxSize(TerrainLayer[] layers, Func<TerrainLayer, Texture> getTexture)
        {
            var maxSize = 1;
            for (var index = 0; index < layers.Length; index++)
            {
                var diffuseTexture = getTexture(layers[index]);
                if (diffuseTexture == null)
                    continue;
                
                maxSize = math.max(maxSize, math.max(diffuseTexture.width, diffuseTexture.height));
            }

            return maxSize;
        }

        private void CopyToTexture(Texture source, GraphicsFormat renderTextureFormat, TextureFormat textureFormat, Texture2DArray destination, int slice, bool srgb)
        {
            var descriptor = new RenderTextureDescriptor(destination.width, destination.height, renderTextureFormat, 0);
            descriptor.sRGB = srgb;
            
            var rt = RenderTexture.GetTemporary(descriptor);
            
            Graphics.Blit(source, rt);
            
            var texture = new Texture2D(destination.width, destination.height, textureFormat, true);
            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            
            RenderTexture.active = previousActive;
            
            texture.Compress(true);
            Graphics.CopyTexture(texture, 0, destination, slice);
            
            Object.DestroyImmediate(texture);
            RenderTexture.ReleaseTemporary(rt);
        }
        
        private int MakeProperSize(int currentSize)
        {
            return currentSize < 4 ? 4 : Mathf.NextPowerOfTwo(currentSize);
        }

        public void Setup(TerrainData terrainData)
        {
            if (terrainData == null)
                return;
         
            isDirty = false;
            
            var layers = terrainData.terrainLayers;
            if (layers.Length == 0)
                return;
            
            Positionings ??= new Vector4[32];
            MaskRemapMinimum ??= new Vector4[32];
            MaskRemapMaximum ??= new Vector4[32];
            DefaultValues ??= new Vector4[32];
            
            var maxDiffuseSize = MakeProperSize(GetMaxSize(layers, x => x.diffuseTexture));
            var maxMaskSize = MakeProperSize(GetMaxSize(layers, x => x.maskMapTexture));
            for (var index = 0; index < layers.Length; index++)
            {
                Positionings[index] = GetPositioning(layers[index], terrainData);
                MaskRemapMinimum[index] = layers[index].maskMapRemapMin;
                MaskRemapMaximum[index] = layers[index].maskMapRemapMax;
                DefaultValues[index] = GetDefaultValues(layers[index], terrainData);
            }   

            var descriptor = new RenderTextureDescriptor();
            descriptor.dimension = TextureDimension.Tex2DArray;
            descriptor.useMipMap = true;
            descriptor.enableRandomWrite = false;
            descriptor.vrUsage = VRTextureUsage.None;
            descriptor.colorFormat = RenderTextureFormat.ARGB32;
            descriptor.sRGB = false;
            descriptor.stencilFormat = GraphicsFormat.None;
            descriptor.autoGenerateMips = true;
            descriptor.msaaSamples = 1;

            if (Diffuse == null || Diffuse.depth != layers.Length)
            {
                Object.DestroyImmediate(Diffuse);

                var diffuseDescriptor = descriptor;
                diffuseDescriptor.graphicsFormat = GraphicsFormat.RGBA_DXT5_UNorm;
                diffuseDescriptor.width = maxDiffuseSize;
                diffuseDescriptor.height = maxDiffuseSize;
                diffuseDescriptor.volumeDepth = layers.Length;
                
                Diffuse = new Texture2DArray(diffuseDescriptor.width, diffuseDescriptor.height, diffuseDescriptor.volumeDepth, TextureFormat.DXT5, true, false);
                Diffuse.wrapMode = TextureWrapMode.Repeat;
                diffuseTextures.Clear();
            }
            
            if (Mask == null || Mask.depth != layers.Length)
            {
                Object.DestroyImmediate(Mask);

                var maskDescriptor = descriptor;
                maskDescriptor.width = maxMaskSize;
                maskDescriptor.height = maxMaskSize;
                maskDescriptor.volumeDepth = layers.Length;
                
                Mask = new Texture2DArray(maskDescriptor.width, maskDescriptor.height, maskDescriptor.volumeDepth, TextureFormat.DXT5, true, true);
                Mask.wrapMode = TextureWrapMode.Repeat;
                maskMaps.Clear();
            }
            
            if (Normal == null || Normal.depth != layers.Length)
            {
                Object.DestroyImmediate(Normal);

                var normalDescriptor = descriptor;
                normalDescriptor.graphicsFormat = GraphicsFormat.RGBA_DXT5_UNorm;
                normalDescriptor.width = maxMaskSize;
                normalDescriptor.height = maxMaskSize;
                normalDescriptor.volumeDepth = layers.Length;
                
                Normal = new Texture2DArray(normalDescriptor.width, normalDescriptor.height, normalDescriptor.volumeDepth, TextureFormat.DXT5, true, true);
                Normal.wrapMode = TextureWrapMode.Repeat;
                normalMaps.Clear();
            }

            var alphamapTextures = terrainData.alphamapTextures;
            if (Alphamap == null || Alphamap.volumeDepth != alphamapTextures.Length)
            {
                Object.DestroyImmediate(Alphamap);
                
                var alphamapDescriptor = descriptor;
                alphamapDescriptor.width = terrainData.alphamapWidth;
                alphamapDescriptor.height = terrainData.alphamapHeight;
                alphamapDescriptor.volumeDepth = alphamapTextures.Length;

                Alphamap = new RenderTexture(alphamapDescriptor);
                Diffuse.wrapMode = TextureWrapMode.Clamp;
            }

            var blitMaterial = new Material(Resources.Load<Shader>("Microdetail/Shaders/Blit"));

            for (var index = 0; index < alphamapTextures.Length; index++)
            {
                var alphamapTexture = alphamapTextures[index];
                if (alphamapTexture == null)
                    alphamapTexture = Texture2D.blackTexture;

                Graphics.Blit(alphamapTexture, Alphamap, blitMaterial, 0, index);
            }

            for (var index = 0; index < layers.Length; index++)
            {
                var diffuseTexture = layers[index].diffuseTexture;
                if (diffuseTexture == null)
                    diffuseTexture = Texture2D.whiteTexture;

                var maskTexture = layers[index].maskMapTexture;
                if (maskTexture == null)
                    maskTexture = DefaultClearMask;

                var normalTexture = layers[index].normalMapTexture;
                if (normalTexture == null)
                    normalTexture = Texture2D.normalTexture;
                
                if (!diffuseTextures.TryGetValue(index, out Texture2D diffuse) || diffuse != diffuseTexture)
                    CopyToTexture(diffuseTexture, GraphicsFormat.R8G8B8A8_SRGB, TextureFormat.ARGB32, Diffuse, index, true);
                
                if (!maskMaps.TryGetValue(index, out Texture2D mask) || mask != maskTexture)
                    CopyToTexture(maskTexture, GraphicsFormat.R8G8B8A8_UNorm, TextureFormat.ARGB32, Mask, index, false);
                
                if (!normalMaps.TryGetValue(index, out Texture2D normal) || normal != normalTexture)
                    CopyToTexture(normalTexture, GraphicsFormat.R8G8B8A8_UNorm, TextureFormat.ARGB32, Normal, index, false);
                
                diffuseTextures[index] = layers[index].diffuseTexture;
                maskMaps[index] = layers[index].maskMapTexture;
                normalMaps[index] = layers[index].normalMapTexture;
            }
        }

        private Vector4 GetPositioning(TerrainLayer layer, TerrainData terrainData)
        {
            var tileSize = layer.tileSize;
            tileSize.x = terrainData.size.x / tileSize.x;
            tileSize.y = terrainData.size.z / tileSize.y;
            var tileOffset = layer.tileOffset;
            return new Vector4(tileSize.x, tileSize.y, tileOffset.x, tileOffset.y);
        }
        
        private Vector4 GetDefaultValues(TerrainLayer layer, TerrainData terrainData)
        {
            return new Vector4(layer.metallic, 1.0f, 1.0f, layer.smoothness);
        }

        public bool MatchesState(TerrainData terrainData)
        {
            if (isDirty)
                return false;
            
            if (!Application.isEditor)
                return true;

            if (terrainData == null)
                return false;

            if (Diffuse == null)
                return false;

            if (Positionings == null)
                return false;
            
            if (DefaultValues == null)
                return false;
            
            var layers = terrainData.terrainLayers;
            if (layers.Length != diffuseTextures.Count ||
                layers.Length != maskMaps.Count ||
                layers.Length != normalMaps.Count)
                return false;

            for (var index = 0; index < layers.Length; index++)
            {
                var positioning = Positionings[index];
                if (Vector3.SqrMagnitude(positioning - GetPositioning(layers[index], terrainData)) > 0.01f)
                    return false;
                
                var defaultValues = DefaultValues[index];
                if (Vector3.SqrMagnitude(defaultValues - GetDefaultValues(layers[index], terrainData)) > 0.01f)
                    return false;
                
                if (layers[index].diffuseTexture != diffuseTextures[index])
                    return false;
                
                if (layers[index].maskMapTexture != maskMaps[index])
                    return false;

                if (layers[index].normalMapTexture != normalMaps[index])
                    return false;
            }
            
            return true;
        }

        public void Dispose()
        {
            TerrainCallbacks.textureChanged -= MarkDirty;
            diffuseTextures.Clear();
            maskMaps.Clear();
            normalMaps.Clear();
            Object.DestroyImmediate(Diffuse);
            Object.DestroyImmediate(Alphamap);
        }
    }
}