using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Round
{
    public partial class PlanetTerrainGenerator
    {
        // ---------------- Pool selection + carving ----------------

        private void ClearWaterPools()
        {
            _lastPoolCount = 0;
            if (waterPoolsParent == null) return;
            for (var i = waterPoolsParent.childCount - 1; i >= 0; i--)
            {
                var child = waterPoolsParent.GetChild(i);
                if (child == null) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        private void SelectAndPreparePools(System.Random rng)
        {
            if (sphereWorld == null) return;

            var min = Mathf.Max(0, minPoolCount);
            var max = Mathf.Max(min, maxPoolCount);
            var poolCount = rng.Next(min, max + 1);
            if (poolCount == 0) return;

            var lpDir = launchpadDirection.sqrMagnitude > 0.0001f ? launchpadDirection.normalized : Vector3.up;
            var launchpadCutoff = Mathf.Cos(launchpadOuterDeg * Mathf.Deg2Rad);
            var spacingCos = Mathf.Cos(poolMinSpacingDeg * Mathf.Deg2Rad);

            // Score every Fibonacci-lattice candidate against pre-carve noise.
            var samples = FibonacciLattice(poolCandidateSamples);
            var candidates = new List<(Vector3 dir, float depression)>(samples.Length);
            const float NeighborOffsetDeg = 5f;
            var neighborCosOuter = Mathf.Cos(NeighborOffsetDeg * Mathf.Deg2Rad);
            var neighborSinOuter = Mathf.Sin(NeighborOffsetDeg * Mathf.Deg2Rad);

            foreach (var n in samples)
            {
                if (Vector3.Dot(n, lpDir) >= launchpadCutoff) continue;

                var height = SampleBaseDisplacement(n);
                BuildTangentBasis(n, out var tangent, out var bitangent);

                var sum = 0f;
                const int neighbors = 6;
                for (var i = 0; i < neighbors; i++)
                {
                    var az = i * (Mathf.PI * 2f / neighbors);
                    var t = tangent * Mathf.Cos(az) + bitangent * Mathf.Sin(az);
                    var sample = (n * neighborCosOuter + t * neighborSinOuter).normalized;
                    sum += SampleBaseDisplacement(sample);
                }
                var avg = sum / neighbors;
                var depression = avg - height;
                if (depression > poolDepressionThreshold) candidates.Add((n, depression));
            }

            candidates.Sort((a, b) => b.depression.CompareTo(a.depression));

            // Greedy pick with min angular spacing so pools don't pile up.
            var picked = new List<(Vector3 dir, float depression)>();
            foreach (var c in candidates)
            {
                var ok = true;
                for (var i = 0; i < picked.Count; i++)
                {
                    if (Vector3.Dot(c.dir, picked[i].dir) > spacingCos) { ok = false; break; }
                }
                if (!ok) continue;
                picked.Add(c);
                if (picked.Count >= poolCount) break;
            }

            // Per-pool: roll size + depth + shape, append to _bowls if pond.
            // Disk-fits-inside-bowl invariant: minimum effective bowl radius
            // (= base * (1 - irregularity)) must exceed diskRadius. With base =
            // diskRadius * bowlRadiusToDiskScale, this means irregularity <
            // 1 - 1/bowlRadiusToDiskScale. Apply that ceiling with a 5% safety margin.
            var maxAllowedIrregularity = (1f - 1f / Mathf.Max(1.01f, bowlRadiusToDiskScale)) * 0.95f;

            foreach (var p in picked)
            {
                var u = (float)rng.NextDouble();
                var biasedU = Mathf.Pow(u, poolSizeBiasPower);
                var diskRadius = Mathf.Lerp(poolDiskMinRadius, poolDiskMaxRadius, biasedU);
                var isPond = diskRadius >= pondCarveThreshold;

                float waterRise;
                if (isPond)
                {
                    // Roll bowl depth and fill fraction independently, so a deep bowl
                    // with low fill ("kettle pond") and a shallow bowl with high fill
                    // ("brimming pool") both appear in the same planet.
                    var depthFactor = Mathf.Lerp(pondDepthFactorMin, pondDepthFactorMax, (float)rng.NextDouble());
                    var bowlDepth = Mathf.Max(diskRadius * pondDepthRadiusRatio * depthFactor, pondMinBowlDepth);
                    var bowlAngularRadius = (diskRadius * bowlRadiusToDiskScale) / Mathf.Max(0.01f, sphereWorld.Radius);
                    var irregularity = Mathf.Lerp(bowlShapeIrregularityMin, bowlShapeIrregularityMax, (float)rng.NextDouble());
                    irregularity = Mathf.Clamp(irregularity, 0f, maxAllowedIrregularity);
                    var freq = Mathf.Lerp(bowlShapeFreqMin, bowlShapeFreqMax, (float)rng.NextDouble());
                    var offset = new Vector2(
                        (float)(rng.NextDouble() * 100.0),
                        (float)(rng.NextDouble() * 100.0));
                    BuildTangentBasis(p.dir, out var tA, out var tB);

                    _bowls.Add(new CarvedBowl
                    {
                        Direction = p.dir,
                        BowlDepth = bowlDepth,
                        AngularRadius = bowlAngularRadius,
                        MaxAngularRadius = bowlAngularRadius * (1f + irregularity),
                        ShapeIrregularity = irregularity,
                        ShapeFrequency = freq,
                        ShapeOffset = offset,
                        TangentA = tA,
                        TangentB = tB,
                    });

                    var fillFraction = Mathf.Lerp(waterFillFractionMin, waterFillFractionMax, (float)rng.NextDouble());
                    var rolledRise = bowlDepth * fillFraction;
                    // Geometry-aware safety: prevent the disk's spherical edge from
                    // poking above the carved bowl rim. The disk extends radially in
                    // its tangent plane, so its outer ring sits at a slightly larger
                    // distance from sphereCenter than the disk center. When fillFraction
                    // is high the rim ends up below the disk edge and the disk reads
                    // as a floating plate. We compute the maximum rise that keeps the
                    // disk strictly inside the bowl walls and clamp the rolled rise to it.
                    //
                    // Derivation: the disk edge angular distance from the bowl center is
                    // ~ diskRadius/R. The bowl carve at that angle equals
                    //   carve = bowlDepth * 0.5 * (cos(π * (diskRadius/R) / bowlAngularRadius) + 1)
                    //         = bowlDepth * 0.5 * (cos(π / bowlRadiusToDiskScale) + 1)
                    // The disk's spherical-distance overhead from being flat in the tangent
                    // plane is ~ diskRadius² / (2R). Putting it together:
                    //   safeMaxRise = bowlDepth * (1 - rimCarveFrac) - tangentLift - margin
                    var rimCarveFrac = 0.5f * (Mathf.Cos(Mathf.PI / Mathf.Max(1.05f, bowlRadiusToDiskScale)) + 1f);
                    var tangentLift = (diskRadius * diskRadius) / (2f * Mathf.Max(0.01f, sphereWorld.Radius));
                    var safeMaxRise = bowlDepth * (1f - rimCarveFrac) - tangentLift - waterRiseSafetyMargin;
                    safeMaxRise = Mathf.Max(0.02f, safeMaxRise);
                    waterRise = Mathf.Clamp(Mathf.Min(rolledRise, safeMaxRise), waterMinRise, waterMaxRise);
                }
                else
                {
                    waterRise = Mathf.Clamp(p.depression * puddleFillFraction, puddleMinRise, puddleMaxRise);
                }

                _picked.Add(new PickedPool
                {
                    Direction = p.dir,
                    DiskRadius = diskRadius,
                    WaterRise = waterRise,
                    IsPond = isPond,
                });
            }
        }

        private void SpawnPoolDisk(PickedPool pool)
        {
            // GetSurfacePoint routes through the height provider, so this lands on
            // the carved bowl floor (or natural noise floor for puddles), not the
            // bare-radius point.
            var posSurface = sphereWorld.GetSurfacePoint(pool.Direction, pool.WaterRise + poolSurfaceLift);
            var rot = sphereWorld.GetSurfaceRotation(pool.Direction, Vector3.forward);
            var dir = pool.Direction.sqrMagnitude > 0.0001f ? pool.Direction.normalized : Vector3.up;

            // Total water column height: the visible water surface is at posSurface;
            // the cylinder bottom buries past the bowl floor by waterColumnExtraDepth
            // so the bottom cap is hidden inside the opaque mesh. waterColumnMinHeight
            // floors the depth so even shallow puddles read as a small volume rather
            // than a paper disk.
            var waterDepth = Mathf.Max(waterColumnMinHeight, pool.WaterRise + waterColumnExtraDepth);
            var halfDepth = waterDepth * 0.5f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = pool.IsPond
                ? $"Pond ({pool.DiskRadius:F1})"
                : $"Puddle ({pool.DiskRadius:F1})";
            DestroyComponentImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(waterPoolsParent, false);
            // Cylinder primitive is centered on its origin and 2 units tall at scale.y=1.
            // We want the top cap exactly at the water surface and the bottom cap
            // waterDepth below; that means the cylinder center sits halfDepth below
            // posSurface along the surface normal, and scale.y = halfDepth gives a
            // world height of waterDepth.
            go.transform.position = posSurface - dir * halfDepth;
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(pool.DiskRadius * 2f, halfDepth, pool.DiskRadius * 2f);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = _waterMaterial;

            var poolComp = go.AddComponent<PlanetWaterPool>();
            poolComp.Configure(pool.Direction, pool.DiskRadius);
        }

        private void EnsureWaterMaterial()
        {
            if (_waterMaterial != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "PlanetWaterMaterial" };

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", 5f);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", 10f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            // Cull off: cylinder primitive's top and bottom caps need to render no
            // matter which side the camera looks from (player can be above, below, or
            // diving through). URP/Lit's transparent default culls back faces, which
            // hid the disk at certain viewing angles - this is the cause of the
            // "no water at all" report when carved bowls dropped the disk below the
            // surrounding rim.
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetInt("_CullMode", 0);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", waterColor);
            mat.color = waterColor;
            // Emission so the water self-lights regardless of where shadows fall in
            // the bowl. While debugging visibility we want unmistakable; once the
            // surface is reliably visible the user can dial waterColor.alpha or set
            // emission intensity lower for a softer look.
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", new Color(waterColor.r, waterColor.g, waterColor.b, 1f) * waterEmissionStrength);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            _waterMaterial = mat;
        }

        // ---------------- Helpers ----------------

        private static Vector3[] FibonacciLattice(int count)
        {
            count = Mathf.Max(2, count);
            var phi = Mathf.PI * (3f - Mathf.Sqrt(5f));
            var result = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var y = 1f - (i / (float)(count - 1)) * 2f;
                var rad = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                var theta = phi * i;
                result[i] = new Vector3(Mathf.Cos(theta) * rad, y, Mathf.Sin(theta) * rad);
            }
            return result;
        }

        private static void BuildTangentBasis(Vector3 n, out Vector3 tangent, out Vector3 bitangent)
        {
            var up = Mathf.Abs(n.y) < 0.99f ? Vector3.up : Vector3.right;
            tangent = Vector3.Cross(n, up).normalized;
            bitangent = Vector3.Cross(n, tangent);
        }

        private static void DestroyComponentImmediate(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
    }
}
