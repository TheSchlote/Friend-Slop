using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Microdetail
{
    public static class GraphicsFormatHelper
    {
        public static int GetBitsPerPixel(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R8_UNorm:
                    return 8;
                case GraphicsFormat.R16_SFloat:
                case GraphicsFormat.R16_UNorm:
                case GraphicsFormat.R5G6B5_UNormPack16:
                case GraphicsFormat.B5G6R5_UNormPack16:
                    return 16;
                case GraphicsFormat.R8G8B8_UNorm:
                    return 24;
                case GraphicsFormat.R8G8B8A8_SInt:
                case GraphicsFormat.R8G8B8A8_SNorm:
                case GraphicsFormat.R8G8B8A8_UInt:
                case GraphicsFormat.R8G8B8A8_UNorm:
                case GraphicsFormat.R8G8B8A8_SRGB:
                case GraphicsFormat.R32_SFloat:
                    return 32;
                case GraphicsFormat.R16G16B16A16_SFloat:
                case GraphicsFormat.R16G16B16A16_SInt:
                case GraphicsFormat.R16G16B16A16_SNorm:
                case GraphicsFormat.R16G16B16A16_UInt:
                case GraphicsFormat.R16G16B16A16_UNorm:
                    return 64;
                case GraphicsFormat.R32G32B32A32_SFloat:
                case GraphicsFormat.R32G32B32A32_SInt:
                case GraphicsFormat.R32G32B32A32_UInt:
                    return 128;
                default:
                    throw new ArgumentException("Unsupported graphics format");
            }
        }
    }
}