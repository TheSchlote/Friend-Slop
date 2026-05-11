using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct FrustumPlane
    {
        public readonly float3 Position;
        public readonly float3 Normal;

        public FrustumPlane(Plane plane, float shift, float normalScale = 1.0f)
        {
            Position = plane.ClosestPointOnPlane(Vector3.zero) - plane.normal * shift;
            Normal = plane.normal * normalScale;
        }

        public FrustumPlane(float3 position, float3 normal)
        {
            Position = position;
            Normal = normal;
        }

        public FrustumPlane Transform(float4x4 transform)
        {
            return new FrustumPlane(
                math.mul(transform, new float4(Position, 1.0f)).xyz, 
                math.mul(transform, new float4(Normal, 0.0f)).xyz);
        }
    }
}