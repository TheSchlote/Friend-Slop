using UnityEngine;

namespace FriendSlop.Round
{
    public partial class PlanetTerrainGenerator
    {
        // ---------------- Displacement ----------------

        // Public surface query: noise + flat mask - bowl carves. Used by mesh build,
        // anchor placement (via SphereWorld.GetSurfacePoint), and IsphereSurfaceHeightProvider.
        private float SampleDisplacement(Vector3 unitNormal)
        {
            return SampleBaseDisplacement(unitNormal) - SampleCarve(unitNormal);
        }

        // Pre-carve noise: used during pool selection so the chosen lows reflect the
        // organic terrain field, not bowls we already dug.
        private float SampleBaseDisplacement(Vector3 unitNormal)
        {
            var fbm = FBm(unitNormal * noiseFrequency + _noiseOffset, octaves, persistence, lacunarity);
            return fbm * noiseAmplitude * LaunchpadFlatMask(unitNormal);
        }

        // Sum of all bowl carve contributions at this query direction.
        private float SampleCarve(Vector3 unitNormal)
        {
            if (_bowls.Count == 0) return 0f;
            var total = 0f;
            for (var i = 0; i < _bowls.Count; i++)
            {
                var bowl = _bowls[i];
                var dot = Mathf.Clamp(Vector3.Dot(unitNormal, bowl.Direction), -1f, 1f);
                var dRad = Mathf.Acos(dot);
                if (dRad > bowl.MaxAngularRadius) continue;

                float effectiveR;
                if (bowl.ShapeIrregularity < 1e-5f)
                {
                    effectiveR = bowl.AngularRadius;
                }
                else
                {
                    var tangent = unitNormal - dot * bowl.Direction;
                    var tLen = tangent.magnitude;
                    if (tLen < 1e-5f)
                    {
                        // Right at the bowl center - rim shape doesn't matter; treat as base radius.
                        effectiveR = bowl.AngularRadius;
                    }
                    else
                    {
                        tangent /= tLen;
                        var u = Vector3.Dot(tangent, bowl.TangentA);
                        var v = Vector3.Dot(tangent, bowl.TangentB);
                        // Sampling Perlin along (cos θ, sin θ) traces the unit circle in noise
                        // space, giving a smooth periodic function of azimuth around the bowl.
                        var noise = Mathf.PerlinNoise(
                            u * bowl.ShapeFrequency + bowl.ShapeOffset.x,
                            v * bowl.ShapeFrequency + bowl.ShapeOffset.y);
                        var shapeFactor = (noise - 0.5f) * 2f * bowl.ShapeIrregularity;
                        effectiveR = bowl.AngularRadius * (1f + shapeFactor);
                    }
                }

                if (dRad >= effectiveR) continue;
                var t = dRad / effectiveR;
                // Cosine falloff: 1 at center, 0 at rim, smooth derivative both ways so
                // the carve blends into the surrounding noise without seams.
                total += bowl.BowlDepth * 0.5f * (Mathf.Cos(t * Mathf.PI) + 1f);
            }
            return total;
        }

        private float LaunchpadFlatMask(Vector3 unitNormal)
        {
            var dir = launchpadDirection.sqrMagnitude > 0.0001f ? launchpadDirection.normalized : Vector3.up;
            var cosAngle = Mathf.Clamp(Vector3.Dot(unitNormal, dir), -1f, 1f);
            var angleDeg = Mathf.Acos(cosAngle) * Mathf.Rad2Deg;
            var inner = launchpadInnerDeg;
            var outer = Mathf.Max(inner + 0.1f, launchpadOuterDeg);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, angleDeg));
        }

        private float FBm(Vector3 p, int oct, float pers, float lac)
        {
            var total = 0f;
            var frequency = 1f;
            var amplitude = 1f;
            var ampSum = 0f;
            for (var i = 0; i < oct; i++)
            {
                total += (Noise3(p * frequency) * 2f - 1f) * amplitude;
                ampSum += amplitude;
                amplitude *= pers;
                frequency *= lac;
            }
            return ampSum > 0f ? total / ampSum : 0f;
        }

        private static float Noise3(Vector3 p)
        {
            var a = Mathf.PerlinNoise(p.y, p.z);
            var b = Mathf.PerlinNoise(p.x + 17.31f, p.z + 5.71f);
            var c = Mathf.PerlinNoise(p.x + 91.13f, p.y + 23.57f);
            return (a + b + c) / 3f;
        }

    }
}
