using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Microdetail
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Transformation
    {
        public float4x4 TransformationMatrix;
    };
}