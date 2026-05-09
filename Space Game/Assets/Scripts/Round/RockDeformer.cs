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

        // Tweak rock surface tint in one place; defaults to a warm dark gray that
        // reads as a discrete object alongside the planet's elevation rock band
        // (which is cool dark gray). Static so all rocks share one material - the
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
            var verts = new Vector3[geom.Vertices.Length];
            for (var i = 0; i < verts.Length; i++)
            {
                var n = geom.Vertices[i];
                var stretched = new Vector3(n.x * axisScale.x, n.y * axisScale.y, n.z * axisScale.z);
                // Sample noise by the original (unit) normal so a single seed maps
                // noise to surface positions consistently regardless of axisScale.
                var noiseSample = SampleNoise3D(n * noiseFrequency + noiseOffset);
                var disp = (noiseSample * 2f - 1f) * noiseAmplitude;
                verts[i] = stretched * (baseRadius * (1f + disp));
            }

            var mesh = new Mesh { name = "ProceduralRock" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(geom.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.sharedMesh = mesh;
            if (meshCollider != null) meshCollider.sharedMesh = mesh;

            EnsureSharedMaterial();
            if (meshRenderer != null && _sharedRockMaterial != null)
            {
                meshRenderer.sharedMaterial = _sharedRockMaterial;
            }

            _built = true;
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
