using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    public readonly struct BrushParameters
    {
        public readonly float2 UV;
        public readonly Texture Texture;

        public BrushParameters(float2 uv, Texture texture)
        {
            UV = uv;
            Texture = texture;
        }
    }
}