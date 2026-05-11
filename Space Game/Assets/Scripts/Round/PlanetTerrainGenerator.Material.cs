using UnityEngine;

namespace FriendSlop.Round
{
    public partial class PlanetTerrainGenerator
    {
        // ---------------- Elevation gradient + surface material ----------------

        private const string TriplanarShaderName = "FriendSlop/PlanetTerrainTriplanar";
        private static bool _triplanarShaderMissingLogged;

        private void ApplyTerrainMaterial()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;

            var gradientTex = BuildElevationTexture();
            var triplanarShader = Shader.Find(TriplanarShaderName);

            if (triplanarShader != null)
            {
                // Per-instance material so each procedural planet keeps its own
                // gradient texture; detail textures are shared statics from the baker
                // (cached cross-instance for a one-time bake).
                var mat = new Material(triplanarShader) { name = "ProceduralPlanetTerrainMaterial" };
                mat.SetTexture("_GradientTex", gradientTex);
                // Per-band texture: authored override if set, otherwise the procedural
                // luminance bake. Pair authored textures with ColorBlend = 1 so their
                // RGB drives the band albedo directly.
                mat.SetTexture("_RockTex",  rockColorTexture  != null ? (Texture)rockColorTexture  : PlanetDetailTextureBaker.GetRock());
                mat.SetTexture("_DirtTex",  dirtColorTexture  != null ? (Texture)dirtColorTexture  : PlanetDetailTextureBaker.GetDirt());
                mat.SetTexture("_GrassTex", grassColorTexture != null ? (Texture)grassColorTexture : PlanetDetailTextureBaker.GetGrass());
                mat.SetTexture("_PeakTex",  peakColorTexture  != null ? (Texture)peakColorTexture  : PlanetDetailTextureBaker.GetPeak());
                mat.SetFloat("_TriplanarTileScale", triplanarTileScale);
                mat.SetFloat("_BandBoundary01", bandBoundary01);
                mat.SetFloat("_BandBoundary12", bandBoundary12);
                mat.SetFloat("_BandBoundary23", bandBoundary23);
                mat.SetFloat("_BandSharpness", bandSharpness);
                mat.SetFloat("_DetailStrength", detailStrength);
                mat.SetFloat("_AmbientBoost", terrainAmbientBoost);
                mat.SetFloat("_RockColorBlend",  rockColorBlend);
                mat.SetFloat("_DirtColorBlend",  dirtColorBlend);
                mat.SetFloat("_GrassColorBlend", grassColorBlend);
                mat.SetFloat("_PeakColorBlend",  peakColorBlend);
                rend.material = mat;
            }
            else
            {
                // Fallback path: no procedural detail textures, just the gradient on
                // the authored URP/Lit material. Logged once so the warning isn't
                // spammed on every Generate(...) call across multiple planets.
                if (!_triplanarShaderMissingLogged)
                {
                    Debug.LogWarning(
                        $"[PlanetTerrainGenerator] Shader '{TriplanarShaderName}' not found. " +
                        "Falling back to gradient-only surface (no procedural detail texture). " +
                        "Verify Assets/Shaders/PlanetTerrainTriplanar.shader compiled cleanly.");
                    _triplanarShaderMissingLogged = true;
                }
                rend.material.mainTexture = gradientTex;
                rend.material.color = Color.white;
            }
        }

        private Texture2D BuildElevationTexture()
        {
            const int Height = 256;
            var tex = new Texture2D(2, Height, TextureFormat.RGB24, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "PlanetElevationGradient",
            };
            // Stops are evenly spaced at 0, .25, .50, .75, 1.0; SampleStops handles the
            // segment lerp. Five colors give us deep -> low -> mid -> high -> peak.
            for (var y = 0; y < Height; y++)
            {
                var t = y / (float)(Height - 1);
                var c = SampleStops(t);
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply(false, true);
            return tex;
        }

        private Color SampleStops(float t)
        {
            t = Mathf.Clamp01(t);
            var sharpness = Mathf.Clamp01(bandSharpness);
            // Sort the boundaries defensively in case the inspector values cross over;
            // also clamp to (0, 1) so we don't divide by zero downstream.
            var b1 = Mathf.Clamp(bandBoundary01, 0.001f, 0.999f);
            var b2 = Mathf.Clamp(bandBoundary12, b1 + 0.001f, 0.999f);
            var b3 = Mathf.Clamp(bandBoundary23, b2 + 0.001f, 0.999f);

            var widthRock = b1;
            var widthDirt = b2 - b1;
            var widthGrass = b3 - b2;
            var widthPeak = 1f - b3;

            // Per-boundary transition half-widths. Each side cannot exceed half the
            // smaller adjacent band so the narrow 10% peak band keeps a real plateau.
            var halfRockDirt = TransitionHalf(sharpness, widthRock, widthDirt);
            var halfDirtGrass = TransitionHalf(sharpness, widthDirt, widthGrass);
            var halfGrassPeak = TransitionHalf(sharpness, widthGrass, widthPeak);

            // Walk the bands by t. Within each band we may be in: previous-boundary
            // transition tail, plateau, or next-boundary transition head.
            if (t < b1)
            {
                // Rock band, possibly transitioning into dirt near b1.
                var transStart = b1 - halfRockDirt;
                if (t <= transStart) return elevationColorRock;
                var transEnd = b1 + halfRockDirt;
                var blend = Mathf.SmoothStep(transStart, transEnd, t);
                return Color.Lerp(elevationColorRock, elevationColorDirt, blend);
            }
            if (t < b2)
            {
                // Dirt band: tail of rock-dirt transition, plateau, or head of dirt-grass transition.
                var leftEnd = b1 + halfRockDirt;
                if (t < leftEnd)
                {
                    var blend = Mathf.SmoothStep(b1 - halfRockDirt, leftEnd, t);
                    return Color.Lerp(elevationColorRock, elevationColorDirt, blend);
                }
                var rightStart = b2 - halfDirtGrass;
                if (t <= rightStart) return elevationColorDirt;
                var rightEnd = b2 + halfDirtGrass;
                var blend2 = Mathf.SmoothStep(rightStart, rightEnd, t);
                return Color.Lerp(elevationColorDirt, elevationColorGrass, blend2);
            }
            if (t < b3)
            {
                // Grass band: tail of dirt-grass transition, plateau, or head of grass-peak transition.
                var leftEnd = b2 + halfDirtGrass;
                if (t < leftEnd)
                {
                    var blend = Mathf.SmoothStep(b2 - halfDirtGrass, leftEnd, t);
                    return Color.Lerp(elevationColorDirt, elevationColorGrass, blend);
                }
                var rightStart = b3 - halfGrassPeak;
                if (t <= rightStart) return elevationColorGrass;
                var rightEnd = b3 + halfGrassPeak;
                var blend2 = Mathf.SmoothStep(rightStart, rightEnd, t);
                return Color.Lerp(elevationColorGrass, elevationColorPeak, blend2);
            }
            // Peak band: tail of grass-peak transition or plateau.
            var peakTransEnd = b3 + halfGrassPeak;
            if (t < peakTransEnd)
            {
                var blend = Mathf.SmoothStep(b3 - halfGrassPeak, peakTransEnd, t);
                return Color.Lerp(elevationColorGrass, elevationColorPeak, blend);
            }
            return elevationColorPeak;
        }

        private static float TransitionHalf(float sharpness, float leftWidth, float rightWidth)
        {
            // Half-width of a boundary's transition zone, capped so it can't extend
            // past the midpoint of either adjacent band. At sharpness=1 we get a
            // hard step (half=0); at sharpness=0 the transition fully consumes the
            // smaller adjacent band.
            var maxHalf = Mathf.Min(leftWidth, rightWidth) * 0.5f;
            return maxHalf * (1f - sharpness);
        }

    }
}
