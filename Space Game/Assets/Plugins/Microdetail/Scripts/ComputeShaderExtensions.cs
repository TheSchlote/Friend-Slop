using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    public static class ComputeShaderExtensions
    {
        private static readonly int[] TwoInts = new int[2];

        public static void SetInt2(this ComputeShader computeShader, int propertyId, int2 value)
        {
            TwoInts[0] = value.x;
            TwoInts[1] = value.y;
            
            computeShader.SetInts(propertyId, TwoInts);
        }

        private static int3 ComputeThreadsCount(this ComputeShader computeShader, int kernel, int3 desiredSize)
        {
            computeShader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);
            return (int3)math.ceil(desiredSize / new float3(x, y, z));
        }

        public static void Dispatch(this ComputeShader computeShader, int kernel, int3 desiredSize)
        {
            var threadsCount = computeShader.ComputeThreadsCount(kernel, desiredSize);
            computeShader.Dispatch(kernel, threadsCount.x, threadsCount.y, threadsCount.z);
        }
    }
}