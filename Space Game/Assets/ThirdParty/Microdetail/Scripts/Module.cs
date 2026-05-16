using UnityEngine;

namespace Microdetail
{
    public interface IDefaultPopulateShaderSetupper
    {
        void ResetToDefault();
        void SetupBuiltInPopulateShader(int kernelIndex, ComputeShader shader);
    }

    public interface IRenderingPrepareCallbackHandler
    {
        void PrepareForDefaultRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo);
        void PrepareForRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo);
    }
    
    [System.Serializable]
    public abstract class Module : ScriptableObject
    {
        
    }
}