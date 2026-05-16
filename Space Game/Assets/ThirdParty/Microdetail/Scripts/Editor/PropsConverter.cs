using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Microdetail
{
    public static class DefaultTextureIds
    {
        public static readonly string SDF = "SDF";
        public static readonly string Albedo = "Albedo";
        public static readonly string Mask = "Mask";
        public static readonly string Metallic = "Metallic";
        public static readonly string AO = "AO";
        public static readonly string Smoothness = "Smoothness";
    }
    
    public enum TextureChannelSource
    {
        None,
        R,
        InversedR,
        G,
        InversedG,
        B,
        InversedB,
        A,
        InversedA
    }

    public class TextureInfo
    {
        public readonly Texture2D Texture;
        public readonly TextureChannelSource Source;

        public TextureInfo(Texture2D texture, TextureChannelSource source)
        {
            Source = source;
            Texture = texture;
        }
    }

    public class TextureMipReference
    {
        public readonly Texture Texture;
        public readonly int MipLevel;

        public TextureMipReference(Texture texture, int mipLevel)
        {
            Texture = texture;
            MipLevel = mipLevel;
        }
    }
    
    public class ConversionData
    {
        public int VoxelSize { get; set; } = 128;
        public int Padding { get; set; } = 2;
        public float Thickness { get; set; }

        public readonly Mesh Mesh;
        public readonly Dictionary<string, TextureInfo> Textures;
        
        public ConversionData(Mesh mesh)
        {
            Mesh = mesh;
            Textures = new Dictionary<string, TextureInfo>();
        }
    }

    public class TextureResult : IDisposable
    {
        public readonly Texture3D Texture;

        public TextureResult(Texture3D texture)
        {
            Texture = texture;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Texture);
        }
    }

    public class ConversionResult : IDisposable
    {
        public readonly Dictionary<string, TextureResult> Results = new Dictionary<string, TextureResult>();

        public void Dispose()
        {
            foreach (var result in Results)
                if (result.Value.Texture != null)
                    result.Value.Dispose();
            
            Results.Clear();
        }
    }

    public enum ConversionStage
    {
        PreparingForConversion,
        GeneratingTextureSDF,
        ReadingMesh,
        GeneratingField,
        FillingVoids,
        ReadingFieldTexture,
        ReadingAlbedoTexture,
        ReadingMaskTexture,
        CleanUp,
    }

    public class PropsConverter
    {
        private static readonly int Padding = Shader.PropertyToID("Padding");
        private static readonly int Thickness = Shader.PropertyToID("Thickness");
        private static readonly int SDF = Shader.PropertyToID("SDF");
        private static readonly int SDFSize = Shader.PropertyToID("SDFSize");
        private static readonly int SDFResult = Shader.PropertyToID("SDFResult");
        private static readonly int TextureSDF = Shader.PropertyToID("TextureSDF");
        private static readonly int ColorResult = Shader.PropertyToID("ColorResult");
        private static readonly int MaskResult = Shader.PropertyToID("MaskResult");
        private static readonly int Triangles = Shader.PropertyToID("Triangles");
        private static readonly int TrianglesCount = Shader.PropertyToID("TrianglesCount");
        
        private static readonly int Texture = Shader.PropertyToID("Texture");
        private static readonly int Metallic = Shader.PropertyToID("Metallic");
        private static readonly int MetallicMask = Shader.PropertyToID("MetallicMask");
        private static readonly int Smoothness = Shader.PropertyToID("Smoothness");
        private static readonly int SmoothnessMask = Shader.PropertyToID("SmoothnessMask");
        private static readonly int AO = Shader.PropertyToID("AO");
        private static readonly int AOMask = Shader.PropertyToID("AOMask");
        private static readonly int StartPosition = Shader.PropertyToID("StartPosition");
        private static readonly int VolumeSize = Shader.PropertyToID("VolumeSize");
        private static readonly int MipLevel = Shader.PropertyToID("MipLevel");
        private static readonly int Step = Shader.PropertyToID("_Step");

        private Vector4 GetMask(TextureChannelSource source)
        {
            switch (source)
            {
                case TextureChannelSource.None:
                    return Vector4.zero;
                case TextureChannelSource.R:
                    return new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                case TextureChannelSource.InversedR:
                    return new Vector4(-1.0f, 0.0f, 0.0f, 0.0f);
                case TextureChannelSource.G:
                    return new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
                case TextureChannelSource.InversedG:
                    return new Vector4(0.0f, -1.0f, 0.0f, 0.0f);
                case TextureChannelSource.B:
                    return new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
                case TextureChannelSource.InversedB:
                    return new Vector4(0.0f, 0.0f, -1.0f, 0.0f);
                case TextureChannelSource.A:
                    return new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                case TextureChannelSource.InversedA:
                    return new Vector4(0.0f, 0.0f, 0.0f, -1.0f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source, null);
            }
        }

        private void DispatchKernel(ComputeShader shader, string kernelName, int3 size, params (int Key, object Value)[] parameters)
        {
            var kernel = shader.FindKernel(kernelName);

            foreach (var entry in parameters)
            {
                if (entry.Value is Texture texture)
                    shader.SetTexture(kernel, entry.Key, texture);
                else if (entry.Value is TextureMipReference textureMipReference)
                    shader.SetTexture(kernel, entry.Key, textureMipReference.Texture, textureMipReference.MipLevel);
                else if (entry.Value is Vector4 vector4)
                    shader.SetVector(entry.Key, vector4);
                else if (entry.Value is Vector3 vector3)
                    shader.SetVector(entry.Key, vector3);
                else if (entry.Value is Vector2 vector2)
                    shader.SetVector(entry.Key, vector2);
                else if (entry.Value is float floatValue)
                    shader.SetFloat(entry.Key, floatValue);
                else if (entry.Value is Vector3Int vector3Int)
                    shader.SetInts(entry.Key, vector3Int.x, vector3Int.y, vector3Int.z);
                else if (entry.Value is int3 vectorInt3)
                    shader.SetInts(entry.Key, vectorInt3.x, vectorInt3.y, vectorInt3.z);
                else if (entry.Value is ComputeBuffer computeBuffer)
                    shader.SetBuffer(kernel, entry.Key, computeBuffer);
                else if (entry.Value is int integer)
                    shader.SetInt(entry.Key, integer);
                else
                    throw new ArgumentException($"Unsupported type: {entry.Value.GetType()}");
            }

            shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out uint z);
            shader.Dispatch(kernel, 
                Mathf.CeilToInt(size.x / (float)x),
                Mathf.CeilToInt(size.y / (float)y), 
                Mathf.CeilToInt(size.z / (float)z));
            
            Graphics.WaitOnAsyncGraphicsFence(Graphics.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations));
        }

        private int3 GetMipSize(int3 size, int mipLevel)
        {
            return (int3)math.floor(math.max(1, (float3)size / math.pow(2, mipLevel)));
        }
        
        private Texture3D Read3DTexture(string name, RenderTexture source)
        {
            var resultTextureSize = new int3(source.width, source.height, source.volumeDepth);
            var rawTexture = new Texture3D(resultTextureSize.x, resultTextureSize.y, resultTextureSize.z,
                source.graphicsFormat,
                TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels,
                source.mipmapCount);
            
            rawTexture.wrapModeU = TextureWrapMode.Clamp;
            rawTexture.wrapModeV = TextureWrapMode.Clamp;
            rawTexture.wrapModeW = TextureWrapMode.Clamp;

            rawTexture.filterMode = FilterMode.Trilinear;
            rawTexture.anisoLevel = 0;

            var bpp = GraphicsFormatHelper.GetBitsPerPixel(source.graphicsFormat);

            var size = GetMipSize(resultTextureSize, 0);
            var data = new NativeArray<byte>(size.x * size.y * size.z * bpp,
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            
            var cachedData = data;
            AsyncGPUReadback.RequestIntoNativeArray(ref data, source, 0, (_) =>
                {
                    rawTexture.SetPixelData(cachedData, 0);
                    cachedData.Dispose();
                });

            AsyncGPUReadback.WaitAllRequests();
            
            rawTexture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            
            rawTexture.name = name;

            var resultTexture = new Texture3D(resultTextureSize.x, resultTextureSize.y, resultTextureSize.z,
                source.graphicsFormat == GraphicsFormat.R8_UNorm ? GraphicsFormat.R_BC4_UNorm : GraphicsFormat.RGBA_DXT5_UNorm,
                TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels,
                source.mipmapCount);
            
            for (var slice = 0; slice < rawTexture.depth; slice++)
            {
                var texture = new Texture2D(rawTexture.width, rawTexture.height, source.graphicsFormat,
                    TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels);

                Graphics.CopyTexture(rawTexture, slice, texture, 0);
                
                EditorUtility.CompressTexture(texture, source.graphicsFormat == GraphicsFormat.R8_UNorm ? TextureFormat.BC4 : TextureFormat.DXT5, TextureCompressionQuality.Best);
                
                Graphics.CopyTexture(texture, 0, resultTexture, slice);
                Object.DestroyImmediate(texture);
            }
            
            resultTexture.name = name;
            
            return resultTexture;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct Triangle
        {
            public readonly float3 A;
            public readonly float2 UVA;
            public readonly float3 B;
            public readonly float2 UVB;
            public readonly float3 C;
            public readonly float2 UVC;

            public Triangle(float3 a, float2 uva, float3 b, float2 uvb, float3 c, float2 uvc)
            {
                A = a;
                UVA = uva;
                B = b;
                UVB = uvb;
                C = c;
                UVC = uvc;
            }
        }

        public async Awaitable<ConversionResult> Convert(ConversionData conversionData, Action<ConversionStage, int, int> conversionCallback)
        {
            await Awaitable.NextFrameAsync();
            
            conversionCallback.Invoke(ConversionStage.PreparingForConversion, 0, 2);
            
            var result = new ConversionResult();

            var computeSDF = Resources.Load<ComputeShader>("Microdetail/Shaders/ComputeSDF");
            var computeFill = Resources.Load<ComputeShader>("Microdetail/Shaders/ComputeFill");
            var fillSDF = new Material(Resources.Load<Shader>("Microdetail/Shaders/FillSDF"));
            var spreadSDF = new Material(Resources.Load<Shader>("Microdetail/Shaders/SpreadSDF"));

            var albedoTexture = conversionData.Textures[DefaultTextureIds.Albedo].Texture;
            var SDFTexture = new RenderTexture(albedoTexture.width, albedoTexture.height, 0,
                RenderTextureFormat.RGFloat);
            var secondarySDFTexture = new RenderTexture(albedoTexture.width, albedoTexture.height, 0,
                RenderTextureFormat.RGFloat);

            var sdfFillIterationCount = 128;
            conversionCallback.Invoke(ConversionStage.GeneratingTextureSDF, 0, sdfFillIterationCount);

            Graphics.Blit(albedoTexture, SDFTexture, fillSDF);

            for (var index = 0; index < sdfFillIterationCount; index++)
            {
                spreadSDF.SetVector(Step, new Vector4(1.0f / secondarySDFTexture.width, 0.0f));
                Graphics.Blit(SDFTexture, secondarySDFTexture, spreadSDF);
                spreadSDF.SetVector(Step, new Vector4(0.0f, 1.0f / secondarySDFTexture.height));
                Graphics.Blit(secondarySDFTexture, SDFTexture, spreadSDF);
                
                conversionCallback.Invoke(ConversionStage.GeneratingTextureSDF, index, sdfFillIterationCount);
            }
            
            conversionCallback.Invoke(ConversionStage.ReadingMesh, 0, 1);
            
            var vertexBuffer = conversionData.Mesh.vertices;
            var min = Vector3.one * 100000.0f;
            var max = -Vector3.one * 100000.0f;
            for (var index = 0; index < vertexBuffer.Length; index++)
            {
                min = Vector3.Min(min, vertexBuffer[index]);
                max = Vector3.Max(max, vertexBuffer[index]);
            }

            var bounds = new Bounds((min + max) * 0.5f, (max - min));
            var boundsSizeMagnitude = bounds.size.magnitude;
            bounds.size /= boundsSizeMagnitude;
            bounds.center /= boundsSizeMagnitude;

            var expectedSize = (int3)math.ceil((bounds.size / math.cmax(bounds.size)) * conversionData.VoxelSize);
            var baseTextureSize = math.ceilpow2(expectedSize);
            var renderTargetDescriptor = GetRenderTargetDescriptor(baseTextureSize);
            var boundsChangeRatio = (float3)expectedSize / baseTextureSize;
            bounds.size /= boundsChangeRatio;

            var SDFDescriptor = renderTargetDescriptor;
            SDFDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            var sdf = new RenderTexture(SDFDescriptor);
            var postprocessedSDF = new RenderTexture(SDFDescriptor);

            var dataDescriptor = renderTargetDescriptor;

            dataDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            var color = new RenderTexture(dataDescriptor);;
            var mask = new RenderTexture(dataDescriptor);

            var triangles = conversionData.Mesh.triangles;
            var uvBuffer = conversionData.Mesh.uv;
            bounds.Encapsulate(new Bounds(bounds.center, Vector3.one * 0.01f));

            var inversedSize = new Vector3(1.0f / bounds.size.x, 1.0f / bounds.size.y, 1.0f / bounds.size.z);

            for (var index = 0; index < vertexBuffer.Length; index++)
            {
                var vertex = vertexBuffer[index] / boundsSizeMagnitude;
                vertexBuffer[index] = Vector3.Scale(vertex - bounds.center + bounds.size * 0.5f, inversedSize);
            }

            var bufferData = new List<Triangle>();

            for (var index = 0; index < triangles.Length; index += 3)
            {
                bufferData.Add(new Triangle(
                    vertexBuffer[triangles[index]],
                    uvBuffer[triangles[index]],
                    vertexBuffer[triangles[index + 1]],
                    uvBuffer[triangles[index + 1]],
                    vertexBuffer[triangles[index + 2]],
                    uvBuffer[triangles[index + 2]]));
            }

            var trianglesBuffer = new ComputeBuffer(bufferData.Count, UnsafeUtility.SizeOf<Triangle>());
            trianglesBuffer.SetData(bufferData);

            var resultSize = new int3(baseTextureSize.x, baseTextureSize.y, baseTextureSize.z);
            var maxVolumeSize = 64;
            var volumeSize = math.min(resultSize, new int3(maxVolumeSize));
            var iterationIndex = 0;
            var iterationsCount = (resultSize + maxVolumeSize - 1) / maxVolumeSize;
            var totalIterationsCount = iterationsCount.x * iterationsCount.y * iterationsCount.z;
            
            for (var x = 0; x < resultSize.x; x += maxVolumeSize)
            {
                for (var y = 0; y < resultSize.y; y += maxVolumeSize)
                {
                    for (var z = 0; z < resultSize.z; z += maxVolumeSize)
                    {
                        DispatchKernel(computeSDF, "ComputeSDF", volumeSize,
                            (TextureSDF, SDFTexture),
                            (SDFResult, sdf),
                            (MaskResult, mask),
                            (ColorResult, color),
                            (Texture, conversionData.Textures[DefaultTextureIds.Albedo].Texture),
                            (Metallic, conversionData.Textures[DefaultTextureIds.Metallic].Texture),
                            (MetallicMask, GetMask(conversionData.Textures[DefaultTextureIds.Metallic].Source)),
                            (Smoothness, conversionData.Textures[DefaultTextureIds.Metallic].Texture),
                            (SmoothnessMask, GetMask(conversionData.Textures[DefaultTextureIds.Smoothness].Source)),
                            (AO, conversionData.Textures[DefaultTextureIds.AO].Texture),
                            (AOMask, GetMask(conversionData.Textures[DefaultTextureIds.AO].Source)),
                            (StartPosition, new Vector4(x, y, z, 0)),
                            (Triangles, trianglesBuffer),
                            (TrianglesCount, bufferData.Count),
                            (VolumeSize, resultSize),
                            (Padding, conversionData.Padding),
                            (Thickness, conversionData.Thickness));

                        if (iterationIndex % 2 == 0) 
                            await Awaitable.NextFrameAsync();
                        
                        conversionCallback?.Invoke(ConversionStage.GeneratingField, iterationIndex++, totalIterationsCount);
                    }
                }
            }
            
            conversionCallback?.Invoke(ConversionStage.FillingVoids, 0, 1);

            DispatchKernel(computeFill, "ComputeFill", resultSize,
                (SDF, sdf),
                (SDFSize, resultSize),
                (SDFResult, postprocessedSDF));

            trianglesBuffer.Dispose();
            
            conversionCallback?.Invoke(ConversionStage.ReadingFieldTexture, 0, 1);
            result.Results.Add(DefaultTextureIds.SDF, new TextureResult(Read3DTexture(DefaultTextureIds.SDF, postprocessedSDF)));
            
            conversionCallback?.Invoke(ConversionStage.ReadingAlbedoTexture, 0, 1);
            result.Results.Add(DefaultTextureIds.Albedo, new TextureResult(Read3DTexture(DefaultTextureIds.Albedo, color)));
            
            conversionCallback?.Invoke(ConversionStage.ReadingMaskTexture, 0, 1);
            result.Results.Add(DefaultTextureIds.Mask, new TextureResult(Read3DTexture(DefaultTextureIds.Mask, mask)));
            
            conversionCallback?.Invoke(ConversionStage.CleanUp, 0, 1);
            
            Object.DestroyImmediate(sdf);
            Object.DestroyImmediate(postprocessedSDF);
            Object.DestroyImmediate(color);
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(SDFTexture);
            Object.DestroyImmediate(secondarySDFTexture);

            return result;
        }

        private static RenderTextureDescriptor GetRenderTargetDescriptor(int3 resultTextureSize)
        {
            var renderTargetDescriptor = new RenderTextureDescriptor();
            renderTargetDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            renderTargetDescriptor.width = resultTextureSize.x;
            renderTargetDescriptor.height = resultTextureSize.y;
            renderTargetDescriptor.volumeDepth = resultTextureSize.z;
            renderTargetDescriptor.dimension = TextureDimension.Tex3D;
            renderTargetDescriptor.msaaSamples = 1;
            renderTargetDescriptor.enableRandomWrite = true;
            renderTargetDescriptor.useMipMap = true;
            return renderTargetDescriptor;
        }
    }
}