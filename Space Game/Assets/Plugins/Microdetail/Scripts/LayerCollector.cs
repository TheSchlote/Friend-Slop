using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Microdetail
{
    public static class LayerCollector
    {
        private static ComputeShader checkComputeShader;
        private static HashSet<LayerRenderer> renderersToCheck = new HashSet<LayerRenderer>();
        private static Stack<LayerRenderer> renderers = new Stack<LayerRenderer>();
        private static readonly int CoverageTargetNameId = Shader.PropertyToID("CoverageTarget");
        private static readonly int TintTargetNameId = Shader.PropertyToID("TintTarget");

        private static ComputeBuffer resultBuffer;
        private static readonly int ResultNameId = Shader.PropertyToID("Result");

        public static void RegisterRendererToCheck(LayerRenderer renderer)
        {
            if (renderersToCheck.Add(renderer))
                renderers.Push(renderer);
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct Result
        {
            public int FilledCoverage;
            public int FilledTint;
        }

        public static void Collect()
        {
            if (renderers.Count == 0)
                return;
            
            var current =  renderers.Pop();
            renderersToCheck.Remove(current);

            if (checkComputeShader == null)
                checkComputeShader = Resources.Load<ComputeShader>("Microdetail/Shaders/CheckTextureCoverage");
            
            resultBuffer ??= new ComputeBuffer(1, UnsafeUtility.SizeOf<Result>());
            var resultData = new Result[resultBuffer.count];
            resultBuffer.SetData(resultData);
            
            var kernel = checkComputeShader.FindKernel("CSMain");
            
            checkComputeShader.SetTexture(kernel, CoverageTargetNameId, current.DensityMapSet.Map);
            checkComputeShader.SetTexture(kernel, TintTargetNameId, current.TintMapSet.Map);
            checkComputeShader.SetBuffer(kernel, ResultNameId, resultBuffer);
            
            checkComputeShader.Dispatch(kernel, new int3(current.MapSize, current.MapSize, 1));
            
            Graphics.WaitOnAsyncGraphicsFence(Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing));
            
            resultBuffer.GetData(resultData);

            var state = LayerMapFlags.None;
            if (resultData[0].FilledCoverage == 0)
                state |= LayerMapFlags.EmptyDensity;
            
            if (resultData[0].FilledTint == 0)
                state |= LayerMapFlags.EmptyTint;

            current.MapFlags = state;
        }
    }
}