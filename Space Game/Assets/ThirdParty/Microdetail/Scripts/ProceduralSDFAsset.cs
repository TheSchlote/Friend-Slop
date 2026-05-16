using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    [System.Serializable]
    public struct TextureMapping
    {
        [field: SerializeField]
        public string Name { get; set; }
        [field: SerializeField]
        public Texture Texture { get; set; }
    }
    
    public class ProceduralSDFAsset : MicrodetailAsset
    {
        [SerializeField]
        private Material material;
        [SerializeField] [Range(0.0f, 1.0f)] private float normalSpherization;
        private Mesh defaultMesh;
        private float3 defaultMeshAspectRatio;
        [SerializeField] 
        private float3 aspectRatio = 1.0f;

        [SerializeField] private List<TextureMapping> textures = new List<TextureMapping>();

        public override Material Material
        {
            get
            {
                material.renderQueue = RenderQueue;
                return material;
            }
        }

        public override float3 AspectRatio => aspectRatio;

        public float NormalSpherization
        {
            get => normalSpherization;
            set => normalSpherization = Mathf.Clamp01(value);
        }

        public override void PrepareForRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo)
        {
            base.PrepareForRendering(material, propertyBlock, graphicalInfo);
            propertyBlock.SetFloat(MicrodetailMaterialProperties.NormalSpherization, NormalSpherization);
            foreach (var texture in textures)
                if (texture.Texture != null)
                    propertyBlock.SetTexture(texture.Name, texture.Texture);
        }
    }
}