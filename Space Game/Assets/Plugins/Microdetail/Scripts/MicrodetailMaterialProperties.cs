using UnityEngine;

namespace Microdetail
{
    public static class MicrodetailMaterialProperties
    {
        public static readonly int Transformation = Shader.PropertyToID("_MicrodetailTransformations");
        public static readonly int Properties = Shader.PropertyToID("_MicrodetailProperties");
        public static readonly int HeightMap = Shader.PropertyToID("_HeightMap");
        public static readonly int SplatMaps = Shader.PropertyToID("_SplatMaps");
        public static readonly int NormalMaps = Shader.PropertyToID("_NormalMaps");
        public static readonly int MaskMaps = Shader.PropertyToID("_MaskMaps");
        public static readonly int TerrainSize = Shader.PropertyToID("_TerrainSize");
        public static readonly int SplatMapsCount = Shader.PropertyToID("_SplatMapsCount");
        public static readonly int NormalizedSize = Shader.PropertyToID("_NormalizedSize");
        public static readonly int LayersCount = Shader.PropertyToID("_LayersCount");
        public static readonly int TerrainPosition = Shader.PropertyToID("_TerrainPosition");
        public static readonly int HeightMapSize = Shader.PropertyToID("_HeightMapSize");
        public static readonly int Textures = Shader.PropertyToID("_Textures");
        public static readonly int SDF = Shader.PropertyToID("_SDF");
        public static readonly int Mask = Shader.PropertyToID("_Mask");
        public static readonly int MaxIterationsCount = Shader.PropertyToID("_MaxIterationsCount");
        public static readonly int Albedo = Shader.PropertyToID("_Albedo");
        public static readonly int TextureSize = Shader.PropertyToID("_TextureSize");
        public static readonly int NormalSpherization = Shader.PropertyToID("_NormalSpherization");
        public static readonly int SplatMapPositioning = Shader.PropertyToID("_SplatMapPositioning");
        public static readonly int MinRemap = Shader.PropertyToID("_MinRemap");
        public static readonly int MaxRemap = Shader.PropertyToID("_MaxRemap");
        public static readonly int BlendDefaults = Shader.PropertyToID("_BlendDefaults");
        public static readonly int SplatmapSize = Shader.PropertyToID("_SplatmapSize");
        public static readonly int MipShift = Shader.PropertyToID("_MipShift");
    }
}