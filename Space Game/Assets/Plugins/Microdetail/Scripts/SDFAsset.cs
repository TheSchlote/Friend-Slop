using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Microdetail
{
    public enum ImpostorResolution
    {
        Texture512 = 512,
        Texture1024 = 1024,
        Texture2048 = 2048,
        Texture4096 = 4096,
        Texture8192 = 8192
    }
    
    public class SDFAsset : MicrodetailAsset
    {
        private Material material;
        private Material impostorMaterial;
        
        [Header("SDF Settings")]
        [SerializeField] private Texture3D sdf;
        [SerializeField] private Texture3D albedo;
        [SerializeField] private Texture3D mask;
        [SerializeField] private int maxSamplingIterationsCount = 10;
        [SerializeField] [Range(0.0f, 1.0f)] private float normalSpherization;
        [SerializeField] private float mipShift = 0.0f;

        private OctahedralImpostor impostor;

        public bool DrawImpostors => false;

        public override Material Material
        {
            get
            {
                if (material != null)
                {
                    material.renderQueue = RenderQueue;
                    return material;
                }
                
                material = new Material(Resources.Load<Material>("Microdetail/Materials/Microdetail sdf"));
                material.renderQueue = RenderQueue;
                return material;
            }
        }
        
        public int MaxSamplingIterationsCount
        {
            get => maxSamplingIterationsCount;
            set => maxSamplingIterationsCount = value;
        }

        public override float3 AspectRatio => math.normalize(new float3(sdf.width, sdf.height, sdf.depth));

        public Texture3D SDF
        {
            get => sdf;
            set => sdf = value;
        }

        public Texture3D Albedo
        {
            get => albedo;
            set => albedo = value;
        }

        public Texture3D Mask
        {
            get => mask;
            set => mask = value;
        }

        public float MipShift
        {
            get => mipShift;
            set => mipShift = value;
        }
        
        public float NormalSpherization
        {
            get => normalSpherization;
            set => normalSpherization = Mathf.Clamp01(value);
        }
        
        public override void PrepareForRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo)
        {
            base.PrepareForRendering(material, propertyBlock, graphicalInfo);
            
            if (Albedo != null)
                Albedo.wrapMode = TextureWrapMode.Clamp;
            
            if (Mask != null)
                Mask.wrapMode = TextureWrapMode.Clamp;
            
            if (SDF != null)
                SDF.wrapMode = TextureWrapMode.Clamp;
            
            propertyBlock.SetTexture(MicrodetailMaterialProperties.SDF, SDF);
            if (Mask != null)
                propertyBlock.SetTexture(MicrodetailMaterialProperties.Mask, Mask);
            
            propertyBlock.SetFloat(MicrodetailMaterialProperties.MipShift, MipShift);
            propertyBlock.SetVector(MicrodetailMaterialProperties.NormalizedSize, (Vector3)AspectRatio);
            propertyBlock.SetInt(MicrodetailMaterialProperties.MaxIterationsCount, maxSamplingIterationsCount);
            
            propertyBlock.SetTexture(MicrodetailMaterialProperties.Albedo, Albedo);
            propertyBlock.SetVector(MicrodetailMaterialProperties.TextureSize, new Vector4(SDF.width, SDF.height, SDF.depth));
            propertyBlock.SetFloat(MicrodetailMaterialProperties.NormalSpherization, NormalSpherization);
        }
    }
}