using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Microdetail
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct WindOctaveParameters
    {
        [Range(0.0f, 5.0f)] public float Strength;
        [Range(0.0f, 20.0f)] public float Frequency;
        [Range(0.0f, 10.0f)] public float Amplitude;

        public WindOctaveParameters(float strength, float frequency, float amplitude)
        {
            Strength = strength;
            Frequency = frequency;
            Amplitude = amplitude;
        }
    }

    [Serializable]
    public class WindModule : Module, IDefaultPopulateShaderSetupper, IDisposable
    {
        private static readonly string WindKeyword = "MICRODETAIL_WIND";
        private static readonly int WindFactor = Shader.PropertyToID("WindFactor");
        private static readonly int WindOctavesParameters = Shader.PropertyToID("WindOctavesParameters");
        private static readonly int WindOctavesCount = Shader.PropertyToID("WindOctavesCount");
        
        [SerializeField, Range(0.0f, 1.0f)] private float windStrength = 1.0f;
        [SerializeField] private List<WindOctaveParameters> windOctaveParameters = new List<WindOctaveParameters>()
            {
                new WindOctaveParameters(1.0f, 0.2f, 0.3f),
                new WindOctaveParameters(0.35f, 1.0f, 1.0f)
            };

        private GraphicsBuffer windOctavesBuffer;

        private void OnDisable()
        {
            windOctavesBuffer?.Dispose();
            windOctavesBuffer = null;
        }

        public float WindStrength
        {
            get => windStrength;
            set => windStrength = math.saturate(value);
        }

        public void ResetToDefault()
        {
            WindStrength = 0.0f;
            windOctaveParameters = new List<WindOctaveParameters>();
        }
        
        public void SetupBuiltInPopulateShader(int kernelIndex, ComputeShader shader)
        {
            if (windOctavesBuffer == null || windOctavesBuffer.count < windOctaveParameters.Count)
            {
                windOctavesBuffer?.Dispose();
                windOctavesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(windOctaveParameters.Count, 1), UnsafeUtility.SizeOf<WindOctaveParameters>());
            }
            
            if (WindOctavesCount > 0 && WindFactor > 0.0f)
                shader.EnableKeyword(WindKeyword);
            else
                shader.DisableKeyword(WindKeyword);
            
            windOctavesBuffer.SetData(windOctaveParameters);
            
            shader.SetFloat(WindFactor, windStrength);
            shader.SetInt(WindOctavesCount, windOctaveParameters.Count);
            shader.SetBuffer(kernelIndex, WindOctavesParameters, windOctavesBuffer);
        }

        public void Dispose()
        {
            windOctavesBuffer?.Dispose();
        }
    }
}