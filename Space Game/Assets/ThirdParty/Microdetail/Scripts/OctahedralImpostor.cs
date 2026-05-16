using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Microdetail
{
    public class OctahedralImpostor : IDisposable
    {
        private static readonly int AlbedoTexture = Shader.PropertyToID("_AlbedoTexture");
        private static readonly int NormalTexture = Shader.PropertyToID("_NormalTexture");
        private static readonly int PropertiesTexture = Shader.PropertyToID("_PropertiesTexture");
        private static readonly int SlicesCountHash = Shader.PropertyToID("_SlicesCount");
        public Texture2D AlbedoMap { get; set; }
        public Texture2D NormalDepth { get; set; }
        public Texture2D PropertiesMap { get; set; }
        public float QuadSize { get; private set; } = 0.2f;
        public float PixelSize => (AlbedoMap != null ? AlbedoMap.width : int.MaxValue) / (float)GridSize;
        
        public int GridSize { get; set; }

        public bool IsValid => AlbedoMap != null && NormalDepth != null && PropertiesMap != null;

        public void SetupMaterial(Material material)
        {
            material.SetTexture(AlbedoTexture, AlbedoMap);
            material.SetTexture(NormalTexture, NormalDepth);
            material.SetTexture(PropertiesTexture, PropertiesMap);
            material.SetInt(SlicesCountHash, GridSize);
        }

        public void Dispose()
        {
            Object.DestroyImmediate(AlbedoMap);
            Object.DestroyImmediate(NormalDepth);
            Object.DestroyImmediate(PropertiesMap);
        }
    }
}