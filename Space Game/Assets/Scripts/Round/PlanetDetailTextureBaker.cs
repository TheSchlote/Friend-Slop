using UnityEngine;

namespace FriendSlop.Round
{
    // Procedurally bakes four luminance-only detail textures (rock / dirt / grass /
    // peak) sampled by the triplanar terrain shader. Cached statically so all
    // procedural planets share the same generated patterns - one ~196 KB bake per
    // band, generated lazily on first use.
    //
    // Output is RGB24 with R == G == B (gray) so the shader can read .r as
    // luminance. We bake to RGB instead of R8 because Texture2D.SetPixels32 only
    // supports ARGB32/RGBA32/RGB24/Alpha8 - safer than SetPixelData and the size
    // hit (~580 KB total across the four bands) is tiny for a one-time bake.
    //
    // Pattern intent per band:
    //   Rock  - layered high-frequency Perlin sharpened with smoothstep, reads as
    //           pebble mottle.
    //   Dirt  - mid-frequency Perlin, gentler edges, reads as soil clumps.
    //   Grass - anisotropic frequency (high X, low Y), gives a "blade" hint when
    //           the texture lands on a tangent surface.
    //   Peak  - low-frequency wide noise compressed to a small range, packed-rock
    //           smoothness with subtle veining.
    public static class PlanetDetailTextureBaker
    {
        private const int Resolution = 256;

        private static Texture2D _rock;
        private static Texture2D _dirt;
        private static Texture2D _grass;
        private static Texture2D _peak;

        public static Texture2D GetRock()  { if (_rock  == null) _rock  = BakeRock();  return _rock;  }
        public static Texture2D GetDirt()  { if (_dirt  == null) _dirt  = BakeDirt();  return _dirt;  }
        public static Texture2D GetGrass() { if (_grass == null) _grass = BakeGrass(); return _grass; }
        public static Texture2D GetPeak()  { if (_peak  == null) _peak  = BakePeak();  return _peak;  }

        private static Texture2D Allocate(string name)
        {
            // linear: true so the gray luminance is read as a linear multiplier in
            // the shader, not gamma-corrected sRGB. Mips are on so triplanar
            // sampling at varying world-space scales doesn't shimmer.
            return new Texture2D(Resolution, Resolution, TextureFormat.RGB24, mipChain: true, linear: true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };
        }

        private static byte LumByte(float n)
        {
            return (byte)(Mathf.Clamp01(n) * 255f);
        }

        private static Texture2D BakeRock()
        {
            var tex = Allocate("PlanetDetail_Rock");
            var pixels = new Color32[Resolution * Resolution];
            const float SeedX = 1701f, SeedY = 2173f;
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var fx = x / (float)Resolution;
                    var fy = y / (float)Resolution;
                    var n  = Mathf.PerlinNoise(fx * 8f  + SeedX,         fy * 8f  + SeedY)         * 0.50f;
                    n     += Mathf.PerlinNoise(fx * 16f + SeedX + 13.7f, fy * 16f + SeedY + 7.3f)  * 0.30f;
                    n     += Mathf.PerlinNoise(fx * 32f + SeedX + 91.5f, fy * 32f + SeedY + 23.1f) * 0.20f;
                    // Sharpen with smoothstep so pebbles read as discrete shapes.
                    n = Mathf.SmoothStep(0.30f, 0.75f, n);
                    var l = LumByte(n);
                    pixels[y * Resolution + x] = new Color32(l, l, l, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        private static Texture2D BakeDirt()
        {
            var tex = Allocate("PlanetDetail_Dirt");
            var pixels = new Color32[Resolution * Resolution];
            const float SeedX = 503f, SeedY = 887f;
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var fx = x / (float)Resolution;
                    var fy = y / (float)Resolution;
                    var n  = Mathf.PerlinNoise(fx * 4f  + SeedX,         fy * 4f  + SeedY)         * 0.60f;
                    n     += Mathf.PerlinNoise(fx * 12f + SeedX + 47.2f, fy * 12f + SeedY + 17.9f) * 0.30f;
                    n     += Mathf.PerlinNoise(fx * 24f + SeedX + 83.1f, fy * 24f + SeedY + 65.4f) * 0.10f;
                    n = Mathf.SmoothStep(0.30f, 0.70f, n);
                    var l = LumByte(n);
                    pixels[y * Resolution + x] = new Color32(l, l, l, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        private static Texture2D BakeGrass()
        {
            var tex = Allocate("PlanetDetail_Grass");
            var pixels = new Color32[Resolution * Resolution];
            const float SeedX = 311f, SeedY = 1019f;
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var fx = x / (float)Resolution;
                    var fy = y / (float)Resolution;
                    // Anisotropy: x frequency >> y frequency gives elongated streaks
                    // along Y. When the texture lands on a tangent surface this reads
                    // as blade direction.
                    var n  = Mathf.PerlinNoise(fx * 16f + SeedX,         fy * 4f  + SeedY)         * 0.60f;
                    n     += Mathf.PerlinNoise(fx * 32f + SeedX + 19.7f, fy * 8f  + SeedY + 31.4f) * 0.30f;
                    n     += Mathf.PerlinNoise(fx * 6f  + SeedX + 55.9f, fy * 6f  + SeedY + 47.2f) * 0.10f;
                    n = Mathf.SmoothStep(0.30f, 0.72f, n);
                    var l = LumByte(n);
                    pixels[y * Resolution + x] = new Color32(l, l, l, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        private static Texture2D BakePeak()
        {
            var tex = Allocate("PlanetDetail_Peak");
            var pixels = new Color32[Resolution * Resolution];
            const float SeedX = 73f, SeedY = 41f;
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var fx = x / (float)Resolution;
                    var fy = y / (float)Resolution;
                    var n  = Mathf.PerlinNoise(fx * 2f  + SeedX,         fy * 2f  + SeedY)         * 0.70f;
                    n     += Mathf.PerlinNoise(fx * 6f  + SeedX + 23.5f, fy * 6f  + SeedY + 11.1f) * 0.20f;
                    n     += Mathf.PerlinNoise(fx * 18f + SeedX + 67.3f, fy * 18f + SeedY + 89.4f) * 0.10f;
                    // Compress contrast so peaks stay relatively uniform packed-rock,
                    // not pebbly. Mean is held near 0.55 so peaks read bright when
                    // multiplied with the elevation gradient color.
                    n = Mathf.Lerp(0.40f, 0.70f, n);
                    var l = LumByte(n);
                    pixels[y * Resolution + x] = new Color32(l, l, l, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }
    }
}
