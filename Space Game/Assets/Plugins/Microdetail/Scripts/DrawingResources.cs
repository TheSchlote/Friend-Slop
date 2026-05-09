using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Microdetail
{
    public class DrawingResources : IDisposable
    {
        private static int maxId = 0;
        
        private readonly int id;
        private GraphicsBuffer indirectArgumentsBuffer;
        private GraphicsBuffer transformationsBuffer;
        private GraphicsBuffer microdetailPropertiesBuffer;
        private GraphicsBuffer statisticsBuffer;
        private GraphicsBuffer frustumSetBuffer;
        private Mesh mesh;
        private uint propsCount;

        public uint PropsCount => propsCount;

        public uint MemorySize
        {
            get
            {
                var newByteSize = (uint)(UnsafeUtility.SizeOf<MicrodetailProperties>() * propsCount +
                                  UnsafeUtility.SizeOf<Transformation>() * propsCount);
                return newByteSize / 1024u / 1024u;
            }
        }

        public Mesh Mesh
        {
            get
            {
                if (mesh == null)
                    mesh = new Mesh() { name = "Microdetail" };
                
                return mesh;
            }
        }

        public GraphicsBuffer IndirectArgumentsBuffer
        {
            get
            {
                if (indirectArgumentsBuffer != null)
                    return indirectArgumentsBuffer;
            
                indirectArgumentsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments,
                    1,
                    GraphicsBuffer.IndirectDrawIndexedArgs.size);

                return indirectArgumentsBuffer;
            }
        }
    
        public GraphicsBuffer TransformationBuffer => transformationsBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)propsCount,
            UnsafeUtility.SizeOf<Transformation>());
    
        public GraphicsBuffer MicrodetailPropertiesBuffer => microdetailPropertiesBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)propsCount,
            UnsafeUtility.SizeOf<MicrodetailProperties>());
        
        public GraphicsBuffer CameraBuffer => frustumSetBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)1,
            UnsafeUtility.SizeOf<ViewParameters>());
        
        public GraphicsBuffer StatisticBuffer => statisticsBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)1,
            UnsafeUtility.SizeOf<StatisticInfo>());

        private int? kernel = null;
        public int PopulateMicrodetailsShaderKernel
        {
            get
            {
                if (kernel.HasValue)
                    return kernel.Value;
                
                kernel = PopulateMicrodetailsShader.FindKernel("CSMain");

                return kernel.Value;
            }
        }
    
        private ComputeShader populateMicrodetailsShader;
        public ComputeShader PopulateMicrodetailsShader
        {
            get
            {
                if (populateMicrodetailsShader != null)
                    return populateMicrodetailsShader;
            
                ReloadPlacementComputeShader();
                return populateMicrodetailsShader;
            }
        }

        public DrawingResources(uint propsCount)
        {
            this.id = maxId++;
            this.propsCount = propsCount == 0 ? 1u : propsCount;
        }

        public void ReloadPlacementComputeShader()
        {
            populateMicrodetailsShader = Object.Instantiate(Resources.Load<ComputeShader>("Microdetail/Shaders/PopulateMicrodetails"));
        }

        public void Reset(uint indexCount)
        {
            var data = new NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs>(1, Allocator.Temp);
            var value = data[0];
            value.indexCountPerInstance = indexCount;
            data[0] = value;
            IndirectArgumentsBuffer.SetData(data);
            TransformationBuffer.SetCounterValue(0);

            var statistics = new NativeArray<StatisticInfo>(1, Allocator.Temp);
            var statistic = statistics[0];
            statistic.DesiredCount = 0;
            statistics[0] = statistic;
            StatisticBuffer.SetData(statistics);
            StatisticBuffer.SetCounterValue(0);
        }

        public void Resize(uint newSize)
        {
            transformationsBuffer?.Dispose();
            microdetailPropertiesBuffer?.Dispose();
            microdetailPropertiesBuffer = null;
            transformationsBuffer = null;
            propsCount = newSize;
        }
        
        public void Dispose()
        {
            statisticsBuffer?.Dispose();
            indirectArgumentsBuffer?.Dispose();
            transformationsBuffer?.Dispose();
            microdetailPropertiesBuffer?.Dispose();
            frustumSetBuffer?.Dispose();
            
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(populateMicrodetailsShader);
        }
    }
}