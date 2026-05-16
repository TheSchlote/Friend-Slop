using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Microdetail
{
    [System.Serializable]
    public class Curve : IDisposable, ISerializationCallbackReceiver
    {
        [SerializeField] private AnimationCurve curve;

        private Texture2D texture;
        private float maxValue;
        
        public (Texture2D Texture, float Max) Calculate()
        {
            if (texture != null)
                return (texture, maxValue);
            
            if (texture == null)
                texture = new Texture2D(128, 1, TextureFormat.RFloat, false, false);

            var max = float.MinValue;
            var width = texture.width;
            var pixelData = new NativeArray<float>(width, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var index = 0; index < width; index++)
            {
                var current = curve.Evaluate((float)index / width);
                pixelData[index] = current;
                max = Math.Max(max, current);
            }

            texture.SetPixelData(pixelData, 0);
            texture.Apply(false);
            pixelData.Dispose();

            maxValue = max;
            
            return (texture, max);
        }

        public void SetFromAnimationCurve(AnimationCurve animationCurve)
        {
            curve.CopyFrom(animationCurve);
        }

        public Curve(float initial)
        {
            curve = AnimationCurve.Linear(0.0f, initial, 1.0f, initial);
        }

        public Curve(float initial, float end)
        {
            curve = AnimationCurve.Linear(0.0f, initial, 1.0f, end);
        }

        public void Dispose()
        {
            Object.DestroyImmediate(texture);
        }

        public void OnBeforeSerialize()
        {
            maxValue = 0.0f;
            texture = null;
        }

        public void OnAfterDeserialize()
        {
            maxValue = 0.0f;
            texture = null;
        }
    }
    
    [System.Serializable]
    public class Gradient : IDisposable, ISerializationCallbackReceiver
    {
        [SerializeField] private UnityEngine.Gradient gradient = new UnityEngine.Gradient();

        private Texture2D texture;
        
        public Texture2D GradientTexture
        {
            get
            {
                if (texture != null)
                    return texture;
                
                if (texture == null)
                    texture = new Texture2D(64, 1, TextureFormat.RGBA32, false);

                var width = texture.width;
                var pixelData = new NativeArray<Color32>(width, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (var index = 0; index < width; index++)
                    pixelData[index] = gradient.Evaluate((float)index / width);

                texture.SetPixelData(pixelData, 0);
                texture.Apply(false);
                pixelData.Dispose();
                    
                return texture;
            }
        }
        
        public void Dispose()
        {
            Object.DestroyImmediate(texture);
        }

        public void OnBeforeSerialize()
        {
            texture = null;
        }

        public void OnAfterDeserialize()
        {
            texture = null;
        }
    }
}