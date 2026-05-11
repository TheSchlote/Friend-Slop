using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Microdetail
{
    public static class BuiltInModuleUtility
    {
        private static List<IDefaultPopulateShaderSetupper> defaultComputeModules;
        private static List<IRenderingPrepareCallbackHandler> defaultRenderingModules;
        
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void Initialize()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Clear;
        }

        private static void Clear()
        {
            if (defaultComputeModules != null)
            {
                foreach (var defaultModule in defaultComputeModules)
                    if (defaultModule is IDisposable disposable)
                        disposable.Dispose();
            }

            defaultComputeModules = null;

            if (defaultRenderingModules != null)
            {
                foreach (var defaultRenderingModule in defaultRenderingModules)
                    if (defaultRenderingModule is IDisposable disposable)
                        disposable.Dispose();
            }
            
            defaultRenderingModules = null;
        }
#endif

        public static void ApplyDefaultModules(int kernelIndex, ComputeShader computeShader)
        {
            if (defaultComputeModules == null)
            {
                defaultComputeModules = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                    from type in assembly.GetTypes()
                    where !type.IsAbstract && !type.IsInterface && type.IsSubclassOf(typeof(Object)) && typeof(IDefaultPopulateShaderSetupper).IsAssignableFrom(type)
                    select type).ToList().ConvertAll(x => ScriptableObject.CreateInstance(x) as IDefaultPopulateShaderSetupper);

                foreach (var defaultModule in defaultComputeModules)
                    defaultModule.ResetToDefault();
            }

            foreach (var defaultModule in defaultComputeModules)
                defaultModule.SetupBuiltInPopulateShader(kernelIndex, computeShader);
        }

        public static void ApplyDefaultModules(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo)
        {
            if (defaultRenderingModules == null)
            {
                defaultRenderingModules = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                    from type in assembly.GetTypes()
                    where !type.IsAbstract && !type.IsInterface && type.IsSubclassOf(typeof(Object)) && typeof(IRenderingPrepareCallbackHandler).IsAssignableFrom(type)
                    select type).ToList().ConvertAll(x => ScriptableObject.CreateInstance(x) as IRenderingPrepareCallbackHandler);

                foreach (var defaultModule in defaultComputeModules)
                    defaultModule.ResetToDefault();
            }

            foreach (var defaultModule in defaultRenderingModules)
                defaultModule.PrepareForDefaultRendering(material, propertyBlock, graphicalInfo);
        }
    }
}