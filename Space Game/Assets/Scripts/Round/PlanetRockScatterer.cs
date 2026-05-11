using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Round
{
    // Scatters procedurally-deformed rocks across a procedural planet's surface
    // with a moderate bias toward the lowest (rock) and highest (peak) elevation
    // bands. Polls PlanetTerrainGenerator each frame; when the terrain seed
    // changes (or the terrain finishes its first generation), this scatters once.
    //
    // Determinism: the scatter seed is derived from PlanetTerrainGenerator's seed
    // via a fixed hash, so every client running the same terrain seed produces
    // the same set of rocks at the same positions with the same shapes. No
    // per-rock NetworkObject needed - rocks are local-only decorations.
    public class PlanetRockScatterer : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private GameObject rockPrefab;

        [Header("Population")]
        [SerializeField, Min(0)] private int targetRockCount = 80;
        [SerializeField, Min(64)] private int candidateSamples = 600;

        [Header("Band Weights (low → high)")]
        [Tooltip("Elevation band [0, BandBoundary01] (rock band).")]
        [SerializeField, Range(0f, 1f)] private float rockBandWeight = 1.0f;
        [Tooltip("Elevation band [BandBoundary01, BandBoundary12] (dirt band).")]
        [SerializeField, Range(0f, 1f)] private float dirtBandWeight = 0.30f;
        [Tooltip("Elevation band [BandBoundary12, BandBoundary23] (grass band).")]
        [SerializeField, Range(0f, 1f)] private float grassBandWeight = 0.20f;
        [Tooltip("Elevation band [BandBoundary23, 1] (peak band).")]
        [SerializeField, Range(0f, 1f)] private float peakBandWeight = 1.0f;

        [Header("Placement")]
        [SerializeField] private Vector2 rockSizeRange = new(0.5f, 1.6f);
        [SerializeField] private float minSpacingDeg = 1.0f;
        [SerializeField] private float launchpadExclusionDeg = 22f;
        [Tooltip("Fraction of the rock's radius buried into the surface so it reads as embedded, not balanced.")]
        [SerializeField, Range(0f, 0.5f)] private float burialDepthFraction = 0.2f;

        [Header("References (auto-resolved when null)")]
        [SerializeField] private SphereWorld sphereWorld;
        [SerializeField] private PlanetTerrainGenerator terrain;
        [SerializeField] private PlanetEnvironment planetEnvironment;
        [SerializeField] private Transform rocksParent;
        [SerializeField] private string rocksParentName = "Rocks";

        // Hash mixer applied to the terrain seed so the rock RNG diverges from any
        // other seed-derived system on the planet. Pure constant; seeded results
        // remain deterministic across clients.
        private const uint ScatterSeedMix = 0x9E3779B9u;

        private uint _lastScatteredSeed;
        private bool _scatteredOnce;
        private readonly List<GameObject> _spawned = new();

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (rockPrefab == null) return;
            if (terrain == null || sphereWorld == null) { ResolveReferences(); return; }
            if (!terrain.HasGenerated) return;

            var terrainSeed = terrain.CurrentSeed;
            if (terrainSeed == 0) return;

            var scatterSeed = unchecked(terrainSeed * ScatterSeedMix + 1u);
            if (_scatteredOnce && scatterSeed == _lastScatteredSeed) return;

            Scatter(scatterSeed);
            _lastScatteredSeed = scatterSeed;
            _scatteredOnce = true;
        }

        private void OnDestroy()
        {
            ClearScatter();
        }

        private void ResolveReferences()
        {
            if (sphereWorld == null) sphereWorld = GetComponent<SphereWorld>();
            if (terrain == null) terrain = GetComponent<PlanetTerrainGenerator>();
            if (planetEnvironment == null) planetEnvironment = GetComponentInParent<PlanetEnvironment>();
            if (rocksParent == null) rocksParent = ResolveRocksParent();
        }

        private Transform ResolveRocksParent()
        {
            // Same auto-create pattern PlanetTerrainGenerator uses for "Water Pools":
            // looks under the env wrapper, creates a sibling if absent so authors
            // don't have to wire it manually in the scene.
            var envRoot = planetEnvironment != null ? planetEnvironment.transform : transform.parent;
            if (envRoot == null) return null;
            var existing = envRoot.Find(rocksParentName);
            if (existing != null) return existing;
            var go = new GameObject(rocksParentName);
            go.transform.SetParent(envRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        private void ClearScatter()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                var r = _spawned[i];
                if (r == null) continue;
                if (Application.isPlaying) Destroy(r);
                else DestroyImmediate(r);
            }
            _spawned.Clear();
        }

        private void Scatter(uint seed)
        {
            ClearScatter();
            ResolveReferences();
            if (rockPrefab == null || sphereWorld == null || terrain == null || rocksParent == null) return;

            var rng = new System.Random(unchecked((int)seed));
            var lpDir = terrain.LaunchpadDirection;
            var lpCutoff = Mathf.Cos(launchpadExclusionDeg * Mathf.Deg2Rad);
            var spacingCos = Mathf.Cos(minSpacingDeg * Mathf.Deg2Rad);

            // Compute baseAcceptRate so target count lines up with band-weighted
            // distribution of the candidates. The weighted-area average tells us
            // the expected accept fraction; we want candidateSamples * rate * avg
            // to roughly equal targetRockCount. Clamped to <=1 so heavily-weighted
            // bands don't overshoot.
            var b1 = terrain.BandBoundary01;
            var b2 = terrain.BandBoundary12;
            var b3 = terrain.BandBoundary23;
            var areaWeightedAvg =
                  b1 * rockBandWeight
                + (b2 - b1) * dirtBandWeight
                + (b3 - b2) * grassBandWeight
                + (1f - b3) * peakBandWeight;
            var baseRate = Mathf.Clamp01(targetRockCount / Mathf.Max(1f, candidateSamples * Mathf.Max(0.001f, areaWeightedAvg)));

            // Per-seed phase offset so different seeds give different rock positions
            // even with the same Fibonacci ordering.
            var phaseOffset = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            var samples = FibonacciLattice(candidateSamples, phaseOffset);

            var placedDirs = new List<Vector3>(targetRockCount);

            for (var i = 0; i < samples.Length; i++)
            {
                if (placedDirs.Count >= targetRockCount) break;

                var dir = samples[i];

                // Skip launchpad zone.
                if (Vector3.Dot(dir, lpDir) >= lpCutoff) continue;

                // Skip pool footprints so rocks don't sit in water.
                if (terrain.IsDirectionInsidePool(dir)) continue;

                var t = terrain.GetNormalizedElevationAt(dir);
                var weight = ComputeBandWeight(t, b1, b2, b3);
                if (weight <= 0.001f) continue;

                if (rng.NextDouble() > weight * baseRate) continue;

                // Min angular spacing reject.
                var ok = true;
                for (var j = 0; j < placedDirs.Count; j++)
                {
                    if (Vector3.Dot(dir, placedDirs[j]) > spacingCos)
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) continue;

                placedDirs.Add(dir);
                SpawnRock(dir, rng);
            }
        }

        private float ComputeBandWeight(float t, float b1, float b2, float b3)
        {
            if (t < b1) return rockBandWeight;
            if (t < b2) return dirtBandWeight;
            if (t < b3) return grassBandWeight;
            return peakBandWeight;
        }

        private void SpawnRock(Vector3 dir, System.Random rng)
        {
            var rockSize = Mathf.Lerp(rockSizeRange.x, rockSizeRange.y, (float)rng.NextDouble());
            // Negative offset buries the rock so it sits in the surface instead of
            // balancing on top. GetSurfacePoint routes through the height provider
            // so this lands on the actual displaced terrain (and respects bowl
            // carves anywhere we ended up close to one even after the pool filter).
            var pos = sphereWorld.GetSurfacePoint(dir, -rockSize * burialDepthFraction);

            // Align prefab's local +Y with the surface normal so deformer's per-axis
            // squash settles vertically along the surface, then twist randomly so
            // rocks aren't all oriented the same way.
            var twistDeg = (float)(rng.NextDouble() * 360.0);
            var alignRot = Quaternion.FromToRotation(Vector3.up, dir);
            var rot = alignRot * Quaternion.AngleAxis(twistDeg, Vector3.up);

            var go = Instantiate(rockPrefab, pos, rot, rocksParent);
            go.name = $"Rock_{_spawned.Count}";

            var deformer = go.GetComponent<RockDeformer>();
            if (deformer != null)
            {
                // Per-rock seed pulled from the deterministic RNG so the same
                // terrain seed always produces the same rock shapes in the same
                // order. Avoids zero - RockDeformer treats 0 as "roll random".
                var rockSeed = rng.Next(1, int.MaxValue);
                deformer.Configure(rockSeed, rockSize);
            }

            _spawned.Add(go);
        }

        private static Vector3[] FibonacciLattice(int count, float phaseOffset)
        {
            count = Mathf.Max(2, count);
            var phi = Mathf.PI * (3f - Mathf.Sqrt(5f));
            var result = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var y = 1f - (i / (float)(count - 1)) * 2f;
                var rad = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                var theta = phi * i + phaseOffset;
                result[i] = new Vector3(Mathf.Cos(theta) * rad, y, Mathf.Sin(theta) * rad);
            }
            return result;
        }
    }
}
