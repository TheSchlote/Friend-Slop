using System;
using UnityEngine;

namespace Microdetail
{
    [Serializable]
    public class TerrainBlendModule : Module, IRenderingPrepareCallbackHandler, IDefaultPopulateShaderSetupper
    {
        private static readonly int TerrainBlendAmount = Shader.PropertyToID("_TerrainBlendAmount");
        private static readonly int TerrainBlendSmoothingLength = Shader.PropertyToID("_TerrainBlendSmoothingLength");
        [SerializeField] private float blendingHeight = 0.03f;
        [SerializeField] private float blendingSmoothing = 0.03f;

        public float BlendingHeight
        {
            get => blendingHeight;
            set => blendingHeight = value;
        }

        public float BlendingSmoothing
        {
            get => blendingSmoothing;
            set => blendingSmoothing = value;
        }

        public void PrepareForDefaultRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo)
        {
            material.DisableKeyword("MICRODETAIL_TERRAIN_BLENDING");
            propertyBlock.SetFloat(TerrainBlendAmount, 0.0f);
            propertyBlock.SetFloat(TerrainBlendSmoothingLength, 0.0f);
        }

        public void PrepareForRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo)
        {
            if (graphicalInfo == null || graphicalInfo.IsEmpty)
                return;
            
            material.EnableKeyword("MICRODETAIL_TERRAIN_BLENDING");
            propertyBlock.SetFloat(TerrainBlendAmount, blendingHeight);
            propertyBlock.SetFloat(TerrainBlendSmoothingLength, blendingSmoothing);
        }

        public void ResetToDefault()
        {
            blendingHeight = 0.0f;
            blendingSmoothing = 0.0f;
        }

        public void SetupBuiltInPopulateShader(int kernelIndex, ComputeShader shader)
        {
            if (blendingHeight < 0.001f && blendingSmoothing < 0.001f)
                shader.DisableKeyword("MICRODETAIL_TERRAIN_BLENDING");
            else
                shader.EnableKeyword("MICRODETAIL_TERRAIN_BLENDING");
        }
    }
}