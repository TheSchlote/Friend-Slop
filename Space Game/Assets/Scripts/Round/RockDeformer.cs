using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Round
{
    // Procedurally builds a deformed-icosphere mesh on a single rock GameObject.
    // The scatterer calls Configure(seed, radius) right after Instantiate; the
    // deformer rebuilds its mesh with that seed so each rock instance is geometry-
    // unique. Materials are shared via a static lazily-created URP/Lit instance
    // so 80 rocks don't allocate 80 materials.
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class RockDeformer : MonoBehaviour
    {
        [Header("Geometry")]
        [SerializeField, Range(1, 4)] private int subdivisions = 2;
        [SerializeField] private float baseRadius = 0.7f;

        [Header("Surface noise")]
        [SerializeField, Range(0f, 0.6f)] private float noiseAmplitude = 0.25f;
        [SerializeField] private float noiseFrequency = 2.5f;

        [Header("Per-axis squash (rolled per seed)")]
        [SerializeField] private Vector3 axisScaleMin = new(0.6f, 0.45f, 0.6f);
        [SerializeField] private Vector3 axisScaleMax = new(1.1f, 0.85f, 1.1f);

        [Header("Determinism")]
        [Tooltip("0 = roll a fresh random seed in Awake. Non-zero = deterministic per-instance shape.")]
        [SerializeField] private int seed;

        [Header("References (auto-resolved when null)")]
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshCollider meshCollider;
        [SerializeField] private MeshRenderer meshRenderer;

        // Authored override material. When set on the prefab, every rock instance
        // uses this material (the prefab's reference is shared via sharedMaterial,
        // so 80 rocks still share one material under the hood). When null, falls
        // back to the procedural runtime-built material below so rocks always
        // render even if no override is wired.
        [Header("Surface")]
        [SerializeField] private Material rockMaterialOverride;
        // Multiplies the spherical UVs we compute per-vertex from the original unit
        // normal. 1 = texture wraps the rock once; >1 = texture tiles more, giving
        // finer detail on a small object. Cylindrical-from-normals UVs have a seam
        // at the back of the sphere (where atan2 wraps from -π to π); for a rock
        // it reads as a thin streak on one azimuth, which is acceptable noise.
        [SerializeField, Range(0.25f, 4f)] private float uvTilingScale = 1.5f;

        // Procedural fallback when no override material is assigned. Defaults to a
        // warm dark gray that reads as a discrete object alongside the planet's
        // elevation rock band. Static so all rocks share one material - the
        // deformed geometry + lighting carry the visual variety.
        private static readonly Color SharedRockColor = new(0.42f, 0.40f, 0.40f, 1f);
        private const float SharedRockSmoothness = 0.10f;
        private static Material _sharedRockMaterial;

        private bool _built;

        private void Awake()
        {
            ResolveReferences();
            if (!_built) Build();
        }

        // Called by PlanetRockScatterer right after Instantiate. The seed determines
        // shape, the radius determines world-space size; both must be deterministic
        // from the planet's seed so all clients see identical rocks.
        public void Configure(int newSeed, float radius)
        {
            seed = newSeed;
            baseRadius = radius;
            Build();
        }

        private void ResolveReferences()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        }

        private void Build()
        {
            ResolveReferences();
            if (meshFilter == null) return;

            // 0 means "roll something local"; the scatterer always passes a nonzero
            // seed so this branch only fires when a rock is dropped into a scene
            // by hand for editor preview.
            var rngSeed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
            var rng = new System.Random(rngSeed);

            var axisScale = new Vector3(
                Mathf.Lerp(axisScaleMin.x, axisScaleMax.x, (float)rng.NextDouble()),
                Mathf.Lerp(axisScaleMin.y, axisScaleMax.y, (float)rng.NextDouble()),
                Mathf.Lerp(axisScaleMin.z, axisScaleMax.z, (float)rng.NextDouble()));

            // Per-rock noise offset so different seeds produce different bumps in the
            // same axis-scaled silhouette.
            var noiseOffset = new Vector3(
                (float)(rng.NextDouble() * 1000.0),
                (float)(rng.NextDouble() * 1000.0),
                (float)(rng.NextDouble() * 1000.0));

            var geom = IcosphereMesh.Build(subdivisions);
            // Use lists so we can append duplicated seam-crossing vertices below.
            var vertsList = new List<Vector3>(geom.Vertices.Length);
            // Raw UVs (before applying uvTilingScale) — computed in [0, 1] so the
            // 0/1 wrap test is just `delta > 0.5`.
            var rawUvsList = new List<Vector2>(geom.Vertices.Length);
            for (var i = 0; i < geom.Vertices.Length; i++)
            {
                var n = geom.Vertices[i];
                var stretched = new Vector3(n.x * axisScale.x, n.y * axisScale.y, n.z * axisScale.z);
                // Sample noise by the original (unit) normal so a single seed maps
                // noise to surface positions consistently regardless of axisScale.
                var noiseSample = SampleNoise3D(n * noiseFrequency + noiseOffset);
                var disp = (noiseSample * 2f - 1f) * noiseAmplitude;
                vertsList.Add(stretched * (baseRadius * (1f + disp)));

                // Cylindrical-spherical UVs from the unit normal direction.
                var u = Mathf.Atan2(n.z, n.x) / (Mathf.PI * 2f) + 0.5f;
                var v = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) / Mathf.PI + 0.5f;
                rawUvsList.Add(new Vector2(u, v));
            }

            // Seam fix: walk triangles, find ones whose vertices straddle the
            // longitude wrap (u differs by > 0.5), and duplicate the wrong-side
            // vertex with u + 1. Without this, triangles at the back of the sphere
            // interpolate across the entire texture width, producing the zigzag
            // band the user reported. Cache duplicates so multiple triangles
            // sharing the same wrong-side vertex share the same split.
            var trisList = new List<int>(geom.Triangles.Length);
            var seamSplitCache = new Dictionary<int, int>();
            for (var t = 0; t < geom.Triangles.Length; t += 3)
            {
                var i0 = geom.Triangles[t];
                var i1 = geom.Triangles[t + 1];
                var i2 = geom.Triangles[t + 2];
                var u0 = rawUvsList[i0].x;
                var u1 = rawUvsList[i1].x;
                var u2 = rawUvsList[i2].x;
                var maxU = Mathf.Max(u0, Mathf.Max(u1, u2));
                if (maxU - u0 > 0.5f) i0 = GetOrCreateSeamSplit(i0, vertsList, rawUvsList, seamSplitCache);
                if (maxU - u1 > 0.5f) i1 = GetOrCreateSeamSplit(i1, vertsList, rawUvsList, seamSplitCache);
                if (maxU - u2 > 0.5f) i2 = GetOrCreateSeamSplit(i2, vertsList, rawUvsList, seamSplitCache);
                trisList.Add(i0);
                trisList.Add(i1);
                trisList.Add(i2);
            }

            // Apply tiling scale now that the seam is split. Multiplying earlier
            // would have changed the seam-detection threshold above.
            var finalUvs = new Vector2[rawUvsList.Count];
            for (var i = 0; i < finalUvs.Length; i++) finalUvs[i] = rawUvsList[i] * uvTilingScale;

            var mesh = new Mesh { name = "ProceduralRock" };
            mesh.SetVertices(vertsList);
            mesh.SetUVs(0, finalUvs);
            mesh.SetTriangles(trisList, 0);
            mesh.RecalculateNormals();
            // Tangents are required for the URP/Lit shader's normal-map sampling.
            // Skipping this leaves M_YFRM_05's normal map driving lighting with a
            // default identity, which reads as a flat surface even when the material
            // is correctly assigned.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            meshFilter.sharedMesh = mesh;
            if (meshCollider != null) meshCollider.sharedMesh = mesh;

            // Override wins when assigned (authored Yughues / similar material).
            // Fallback path runs only if no override is wired, so the prefab keeps
            // rendering even in scenes that don't have the override package.
            if (meshRenderer != null)
            {
                if (rockMaterialOverride != null)
                {
                    meshRenderer.sharedMaterial = rockMaterialOverride;
                }
                else
                {
                    EnsureSharedMaterial();
                    if (_sharedRockMaterial != null)
                        meshRenderer.sharedMaterial = _sharedRockMaterial;
                }
            }

            _built = true;
        }

        // Returns the duplicate index for a seam-crossing vertex, creating one on
        // first encounter. The duplicate sits at the same world position as the
        // original but with u shifted by +1 so triangles spanning the longitude
        // wrap interpolate continuously instead of across the entire texture.
        private static int GetOrCreateSeamSplit(
            int originalIndex,
            List<Vector3> verts,
            List<Vector2> rawUvs,
            Dictionary<int, int> cache)
        {
            if (cache.TryGetValue(originalIndex, out var existing)) return existing;
            var newIndex = verts.Count;
            verts.Add(verts[originalIndex]);
            var origUv = rawUvs[originalIndex];
            rawUvs.Add(new Vector2(origUv.x + 1f, origUv.y));
            cache[originalIndex] = newIndex;
            return newIndex;
        }

        private static void EnsureSharedMaterial()
        {
            if (_sharedRockMaterial != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "RockSurfaceMaterial" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", SharedRockColor);
            mat.color = SharedRockColor;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", SharedRockSmoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            _sharedRockMaterial = mat;
        }

        private static float SampleNoise3D(Vector3 p)
        {
            // Three orthogonal Perlin samples with irrational offsets - same trick
            // PlanetTerrainGenerator uses for terrain noise. Cheap, smooth, and
            // periodic seams don't align across axes.
            var a = Mathf.PerlinNoise(p.y, p.z);
            var b = Mathf.PerlinNoise(p.x + 17.31f, p.z + 5.71f);
            var c = Mathf.PerlinNoise(p.x + 91.13f, p.y + 23.57f);
            return (a + b + c) / 3f;
        }
    }
}
