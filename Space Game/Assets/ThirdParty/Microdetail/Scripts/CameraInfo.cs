using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CameraInfo
    {
        private static Plane[] TempPlanes = new Plane[6];

        public readonly Camera Camera;
        public readonly ViewParameters ViewInfo;
        public readonly float4x4 LocalToWorldMatrix;
        public readonly float4x4 WorldToLocalMatrix;

        public CameraInfo(Camera camera, float shift, float farPlaneDistance)
        {
            Camera = camera;
            GeometryUtility.CalculateFrustumPlanes(camera, TempPlanes);
            
            var worldToViewportMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;
            
            ViewInfo = new ViewParameters(TempPlanes[4], TempPlanes[5], TempPlanes[0], TempPlanes[1], TempPlanes[3], TempPlanes[2], 
                shift, 
                farPlaneDistance,
                worldToViewportMatrix,
                camera.cameraToWorldMatrix,
                new float2(camera.pixelWidth, camera.pixelHeight));
            
            LocalToWorldMatrix = camera.transform.localToWorldMatrix;
            WorldToLocalMatrix = camera.transform.worldToLocalMatrix;
        }
    }
}