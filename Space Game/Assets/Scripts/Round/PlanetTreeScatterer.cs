using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Round
{
    // Scatters procedurally-built trees (cylinder trunk + sphere canopies) across a
    // procedural planet's surface, biased to the grass elevation band with a small
    // dirt-band presence and zero rock/peak placement. Mirrors PlanetRockScatterer's
    // polling + seeded-scatter pattern; uses a different scatter-seed mix so trees
    // and rocks pull from independent RNG sequences derived from the same terrain
    // seed (so the same terrain seed produces identical trees + rocks on every
    // client).
    //
    // Trees are inline primitives (no prefab) - matches the existing
    // FriendSlop.Core.PlanetTreeSpawner aesthetic on StarterJunk while staying
    // independent of that class so this scatterer can live in the Round assembly
    // and read PlanetTerrainGenerator's elevation-band info directly.
    public class PlanetTreeScatterer : MonoBehaviour
    {
        [Header("Population")]
        [SerializeField, Min(0)] private int targetTreeCount = 120;
        [SerializeField, Min(64)] private int candidateSamples = 800;

        [Header("Band Weights (low → high)")]
        [Tooltip("Elevation band [0, BandBoundary01] (rock band) - default 0 = no trees.")]
        [SerializeField, Range(0f, 1f)] private float rockBandWeight = 0.0f;
        [Tooltip("Elevation band [BandBoundary01, BandBoundary12] (dirt band) - sparse.")]
        [SerializeField, Range(0f, 1f)] private float dirtBandWeight = 0.20f;
        [Tooltip("Elevation band [BandBoundary12, BandBoundary23] (grass band) - dominant.")]
        [SerializeField, Range(0f, 1f)] private float grassBandWeight = 1.0f;
        [Tooltip("Elevation band [BandBoundary23, 1] (peak band) - default 0 = no trees.")]
        [SerializeField, Range(0f, 1f)] private float peakBandWeight = 0.0f;

        [Header("Tree Geometry (per-tree rolls)")]
        [SerializeField] private float minTrunkHeight = 1.0f;
        [SerializeField] private float maxTrunkHeight = 2.4f;
        [SerializeField] private float minTrunkRadius = 0.07f;
        [SerializeField] private float maxTrunkRadius = 0.16f;
        [SerializeField] private float minCanopyRadius = 0.65f;
        [SerializeField] private float maxCanopyRadius = 1.55f;
        [Tooltip("How far the trunk's base sinks below the surface so it doesn't sit on roots.")]
        [SerializeField] private float trunkSinkDepth = 0.08f;

        [Header("Tree Color (flat, not gradient-derived)")]
        [SerializeField] private Color trunkColor = new(0.28f, 0.16f, 0.05f);
        [SerializeField] private Color canopyColor = new(0.18f, 0.52f, 0.12f);

        [Header("Placement")]
        [SerializeField] private float minSpacingDeg = 1.5f;
        [SerializeField] private float launchpadExclusionDeg = 22f;

        [Header("References (auto-resolved when null)")]
        [SerializeField] private SphereWorld sphereWorld;
        [SerializeField] private PlanetTerrainGenerator terrain;
        [SerializeField] private PlanetEnvironment planetEnvironment;
        [SerializeField] private Transform treesParent;
        [SerializeField] private string treesParentName = "Trees";

        // Different seed mix from PlanetRockScatterer's 0x9E3779B9 so trees and rocks
        // don't lock-step their RNG sequences while still being deterministic from
        // the shared terrain seed.
        private const uint ScatterSeedMix = 0x85EBCA77u;

        // Static shared materials so 120 trees * 2 surfaces don't allocate 240
        // materials. The scatterer constructs them lazily on first spawn using the
        // SerializeField colors, so re-tinting requires either a domain reload (in
        // editor) or rebooting the app.
        private static Material _sharedTrunkMaterial;
        private static Material _sharedCanopyMaterial;

        private uint _lastScatteredSeed;
        private bool _scatteredOnce;
        private readonly List<GameObject> _spawned = new();

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
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
            if (treesParent == null) treesParent = ResolveTreesParent();
        }

        private Transform ResolveTreesParent()
        {
            // Same auto-create pattern PlanetTerrainGenerator and PlanetRockScatterer
            // use for "Water Pools" and "Rocks": looks under the env wrapper, creates
            // a sibling if absent so authors don't have to wire it manually.
            var envRoot = planetEnvironment != null ? planetEnvironment.transform : transform.parent;
            if (envRoot == null) return null;
            var existing = envRoot.Find(treesParentName);
            if (existing != null) return existing;
            var go = new GameObject(treesParentName);
            go.transform.SetParent(envRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        private void ClearScatter()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                var t = _spawned[i];
                if (t == null) continue;
                if (Application.isPlaying) Destroy(t);
                else DestroyImmediate(t);
            }
            _spawned.Clear();
        }

        private void Scatter(uint seed)
        {
            ClearScatter();
            ResolveReferences();
            if (sphereWorld == null || terrain == null || treesParent == null) return;

            EnsureMaterials();

            var rng = new System.Random(unchecked((int)seed));
            var lpDir = terrain.LaunchpadDirection;
            var lpCutoff = Mathf.Cos(launchpadExclusionDeg * Mathf.Deg2Rad);
            var spacingCos = Mathf.Cos(minSpacingDeg * Mathf.Deg2Rad);

            var b1 = terrain.BandBoundary01;
            var b2 = terrain.BandBoundary12;
            var b3 = terrain.BandBoundary23;
            var areaWeightedAvg =
                  b1 * rockBandWeight
                + (b2 - b1) * dirtBandWeight
                + (b3 - b2) * grassBandWeight
                + (1f - b3) * peakBandWeight;
            var baseRate = Mathf.Clamp01(targetTreeCount / Mathf.Max(1f, candidateSamples * Mathf.Max(0.001f, areaWeightedAvg)));

            var phaseOffset = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            var samples = FibonacciLattice(candidateSamples, phaseOffset);

            var placedDirs = new List<Vector3>(targetTreeCount);

            for (var i = 0; i < samples.Length; i++)
            {
                if (placedDirs.Count >= targetTreeCount) break;

                var dir = samples[i];

                if (Vector3.Dot(dir, lpDir) >= lpCutoff) continue;
                if (terrain.IsDirectionInsidePool(dir)) continue;

                var t = terrain.GetNormalizedElevationAt(dir);
                var weight = ComputeBandWeight(t, b1, b2, b3);
                if (weight <= 0.001f) continue;

                if (rng.NextDouble() > weight * baseRate) continue;

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
                SpawnTree(dir, rng);
            }
        }

        private float ComputeBandWeight(float t, float b1, float b2, float b3)
        {
            if (t < b1) return rockBandWeight;
            if (t < b2) return dirtBandWeight;
            if (t < b3) return grassBandWeight;
            return peakBandWeight;
        }

        private void SpawnTree(Vector3 dir, System.Random rng)
        {
            // GetSurfacePoint routes through the height provider, so the tree sits on
            // the actual displaced terrain rather than the bare-radius point.
            var surface = sphereWorld.GetSurfacePoint(dir, 0f);
            var alignRot = Quaternion.FromToRotation(Vector3.up, dir);

            var trunkH = Lerp(minTrunkHeight, maxTrunkHeight, rng);
            var trunkR = Lerp(minTrunkRadius, maxTrunkRadius, rng);
            var canopyR = Lerp(minCanopyRadius, maxCanopyRadius, rng);

            var treeRoot = new GameObject($"Tree_{_spawned.Count}");
            treeRoot.transform.SetParent(treesParent, true);
            treeRoot.transform.position = surface;
            treeRoot.transform.rotation = alignRot;

            PlaceTrunk(treeRoot.transform, surface, dir, alignRot, trunkH, trunkR);
            PlaceCanopy(treeRoot.transform, surface, dir, trunkH, canopyR, rng);

            _spawned.Add(treeRoot);
        }

        private void PlaceTrunk(Transform parent, Vector3 surface, Vector3 up, Quaternion rot,
            float height, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(parent, true);
            // Sinks the trunk's bottom into the surface by trunkSinkDepth so the
            // base reads as embedded rather than balancing on roots. Cylinder
            // primitive total height = scale.y * 2, so scale.y = height/2 gives
            // a trunk of `height` world units.
            go.transform.position = surface + up * (height * 0.5f - Mathf.Max(0f, trunkSinkDepth));
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(radius, height * 0.5f, radius);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _sharedTrunkMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private void PlaceCanopy(Transform parent, Vector3 surface, Vector3 up,
            float trunkHeight, float radius, System.Random rng)
        {
            // Primary canopy sphere sits centered above the trunk top.
            PlaceCanopySphere(parent, surface + up * (trunkHeight + radius * 0.8f), radius);

            // Secondary canopy at a small offset for visual fullness; same trick the
            // existing PlanetTreeSpawner uses on StarterJunk.
            var tangent = OrthoTangent(up, rng);
            var offset = tangent * radius * Lerp(0.3f, 0.55f, rng)
                       + up * radius * Lerp(-0.3f, 0.2f, rng);
            PlaceCanopySphere(parent,
                surface + up * (trunkHeight + radius * 0.65f) + offset,
                radius * Lerp(0.55f, 0.75f, rng));
        }

        private static void PlaceCanopySphere(Transform parent, Vector3 pos, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * radius * 2f;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _sharedCanopyMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private void EnsureMaterials()
        {
            if (_sharedTrunkMaterial == null) _sharedTrunkMaterial = MakeFlatMat(trunkColor, "TreeTrunkMaterial");
            if (_sharedCanopyMaterial == null) _sharedCanopyMaterial = MakeFlatMat(canopyColor, "TreeCanopyMaterial");
        }

        private static Material MakeFlatMat(Color color, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        // Helpers ---------------------------------------------------------------

        private static float Lerp(float a, float b, System.Random rng)
        {
            return Mathf.Lerp(a, b, (float)rng.NextDouble());
        }

        private static Vector3 OrthoTangent(Vector3 up, System.Random rng)
        {
            // Pick an arbitrary ray and cross with `up` to get a perpendicular
            // tangent. Fall back to a known non-parallel axis if the random ray
            // happens to be near-collinear (rare but worth handling cleanly).
            var seed = new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0));
            var tangent = Vector3.Cross(up, seed);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(up, Vector3.right);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(up, Vector3.forward);
            return tangent.normalized;
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
