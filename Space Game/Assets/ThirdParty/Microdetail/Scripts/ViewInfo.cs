using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ViewParameters
    {
        private static FrustumPlane[] TempPlanes = new FrustumPlane[6];
        
        public readonly FrustumPlane Near;
        public readonly FrustumPlane Far;
        public readonly FrustumPlane Left;
        public readonly FrustumPlane Right;
        public readonly FrustumPlane Up;
        public readonly FrustumPlane Down;
        
        public readonly float4x4 WorldToViewportMatrix;
        public readonly float4x4 CameraToWorldMatrix;
        public readonly float2 ScreenResolution;

        public ViewParameters(Plane near, Plane far, Plane left, Plane right, Plane up, Plane down, float shift, float farPlaneDistance, float4x4 worldToViewMatrix, float4x4 cameraToWorldMatrix, float2 screenResolution)
        {
            Near = new FrustumPlane(near, shift);

            var farFrustumPlane = new FrustumPlane(near, -farPlaneDistance, -1.0f);
            
            Far = farFrustumPlane;
            Left = new FrustumPlane(left, shift);
            Right = new FrustumPlane(right, shift);
            Up = new FrustumPlane(up, shift);
            Down = new FrustumPlane(down, shift);
            WorldToViewportMatrix = worldToViewMatrix;
            CameraToWorldMatrix = cameraToWorldMatrix;
            ScreenResolution = screenResolution;
        }
        
        public Bounds GetViewInfo()
        {
            TempPlanes[0] = Near;
            TempPlanes[1] = Far;
            TempPlanes[2] = Up;
            TempPlanes[3] = Left;
            TempPlanes[4] = Down;
            TempPlanes[5] = Right;

            return FrustumUtility.ComputeFrustumPropertiesFromPlanes(TempPlanes);
        }
    }
}