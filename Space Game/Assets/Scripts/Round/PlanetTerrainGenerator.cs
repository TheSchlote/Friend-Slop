using System.Collections.Generic;
using FriendSlop.Core;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    // Procedurally displaces a subdivided icosphere on the parent SphereWorld at
    // round start, registers itself as the world's height provider, drops a varying
    // mix of puddles and ponds into the deepest local minima, and bakes an elevation
    // gradient texture so terrain bands read at a glance. The server picks a seed and
    // replicates it via NetworkVariable; every client regenerates the same mesh
    // deterministically without any geometry replication.
    //
    // Generation order (mirrored exactly on every client because every input is
    // derived from the seed):
    //   1. Pick pool centers using the *pre-carve* noise field (so picks reflect the
    //      naturally-low spots, not where we previously chose to dig).
    //   2. Per pool, roll size + carve params into _picked / _bowls.
    //   3. Build mesh through SampleDisplacement (base + carves) so pond bottoms
    //      are baked into the geometry.
    //   4. Bake the elevation gradient and apply it via vertex UVs.
    //   5. Spawn water disks; ponds use bowlDepth, puddles use natural depression.
    //
    // Conventions for the planet sphere GameObject this lives on:
    //   - localScale = 1: the procedural mesh is authored at full radius, not unit scale.
    //   - MeshCollider, no SphereCollider: the bumpy surface IS the collision shape.
    //   - SphereWorld component on the same GO so we can read Radius and register as
    //     the height provider.
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(SphereWorld))]
    public class PlanetTerrainGenerator : NetworkBehaviour, ISphereSurfaceHeightProvider
    {
        [Header("Geometry")]
        [SerializeField, Range(1, 6)] private int subdivisions = 5;

        [Header("Displacement")]
        [SerializeField] private float noiseFrequency = 1.8f;
        [SerializeField] private float noiseAmplitude = 7.0f;
        [SerializeField, Range(1, 5)] private int octaves = 3;
        [SerializeField, Range(0.1f, 0.95f)] private float persistence = 0.5f;
        [SerializeField, Range(1f, 4f)] private float lacunarity = 2.0f;

        [Header("Launchpad Flat Zone (local-space direction)")]
        [SerializeField] private Vector3 launchpadDirection = Vector3.up;
        [SerializeField, Range(0f, 60f)] private float launchpadInnerDeg = 12f;
        [SerializeField, Range(0f, 90f)] private float launchpadOuterDeg = 28f;

        [Header("Elevation Gradient (low to high)")]
        // 4 named bands with non-uniform widths set by 3 boundary positions on the
        // normalized [0,1] elevation axis. Bands are addressed via vertex UV.y in
        // BuildMesh; the gradient is baked once into a 2x256 mainTexture per planet.
        // Defaults: bottom 15% rock, next 25% beige dirt, middle 50% grass, top 10%
        // light grey peaks.
        [SerializeField] private Color elevationColorRock = new(0.28f, 0.28f, 0.30f, 1f);
        [SerializeField] private Color elevationColorDirt = new(0.65f, 0.55f, 0.40f, 1f);
        [SerializeField] private Color elevationColorGrass = new(0.50f, 0.70f, 0.32f, 1f);
        [SerializeField] private Color elevationColorPeak = new(0.78f, 0.80f, 0.82f, 1f);
        [SerializeField, Range(0f, 1f)] private float bandBoundary01 = 0.15f;
        [SerializeField, Range(0f, 1f)] private float bandBoundary12 = 0.40f;
        [SerializeField, Range(0f, 1f)] private float bandBoundary23 = 0.90f;
        // Transition zone width: 0 = soft, gradual transitions that span the smaller
        // adjacent band; 1 = hard step at each boundary. Each boundary's transition
        // half-width is capped by half the smaller adjacent band width, so the 10%
        // peak band still gets clean edges instead of being swallowed by a wide
        // grass→peak transition.
        [SerializeField, Range(0f, 1f)] private float bandSharpness = 0.7f;

        [Header("Surface Texture (triplanar shader)")]
        // Triplanar tile scale: detail texture repeats every (1/scale) world units.
        // Default 0.25 = one tile per 4 world units.
        [SerializeField, Range(0.01f, 1f)] private float triplanarTileScale = 0.25f;
        // Detail strength: 0 = pure gradient color, 1 = detail luminance fully drives
        // surface brightness (mean preserved at the gradient color, range expanded).
        [SerializeField, Range(0f, 1f)] private float detailStrength = 0.5f;
        // Constant ambient added on top of SH so dark sides aren't pitch black.
        [SerializeField, Range(0f, 1f)] private float terrainAmbientBoost = 0.25f;

        [Header("Pool Selection")]
        [SerializeField, Min(0)] private int minPoolCount = 4;
        [SerializeField, Min(0)] private int maxPoolCount = 8;
        [SerializeField, Min(16)] private int poolCandidateSamples = 256;
        [SerializeField] private float poolMinSpacingDeg = 18f;
        // Depression-score gate (against the pre-carve noise field): ignore candidates
        // whose neighbor-relative dip is below this. Filters noise-grade jitter so we
        // only consider real lows.
        [SerializeField] private float poolDepressionThreshold = 0.6f;

        [Header("Pool Sizing (puddles vs ponds)")]
        [SerializeField] private float poolDiskMinRadius = 0.6f;
        [SerializeField] private float poolDiskMaxRadius = 4.5f;
        // u^N curve on the [0,1] roll; higher = more puddles, fewer ponds.
        [SerializeField, Range(1f, 6f)] private float poolSizeBiasPower = 2.2f;
        // Pools at or above this disk radius get a carved bowl; smaller ones stay
        // surface-level "puddles" sitting in the natural noise dip.
        [SerializeField] private float pondCarveThreshold = 1.6f;

        [Header("Pond Bowl Carve")]
        [SerializeField] private float pondDepthRadiusRatio = 0.6f;
        [SerializeField] private float pondMinBowlDepth = 0.8f;
        // Per-pond depth multiplier rolled in [min, max]. Wider range = more variety in
        // bowl depth across pools, so they stop feeling like the same ditch every time.
        [SerializeField] private float pondDepthFactorMin = 0.5f;
        [SerializeField] private float pondDepthFactorMax = 1.4f;
        [SerializeField] private float bowlRadiusToDiskScale = 1.5f;
        [SerializeField, Range(0f, 0.5f)] private float bowlShapeIrregularityMin = 0.12f;
        [SerializeField, Range(0f, 0.5f)] private float bowlShapeIrregularityMax = 0.28f;
        [SerializeField] private float bowlShapeFreqMin = 1.5f;
        [SerializeField] private float bowlShapeFreqMax = 3.0f;

        [Header("Water Disk")]
        // Per-pond fill fraction rolled in [min, max] = "% of carved bowl filled".
        // Wider range gives a mix of lakes that look almost brimming and ponds that
        // sit deep in their bowls with visible shore.
        [SerializeField, Range(0f, 1f)] private float waterFillFractionMin = 0.20f;
        [SerializeField, Range(0f, 1f)] private float waterFillFractionMax = 0.50f;
        [SerializeField] private float waterMinRise = 0.10f;
        [SerializeField] private float waterMaxRise = 4.5f;
        // Distance the bowl rim should sit above the disk's outer edge after the
        // geometry safety clamp runs. Larger margin = more visible bowl wall above
        // water, smaller margin = water can rise closer to the rim. The clamp
        // computes the actual max rise from the disk/bowl/sphere geometry and only
        // engages when a rolled fill would push the disk's edge above the rim.
        [SerializeField] private float waterRiseSafetyMargin = 0.08f;
        // Puddles fill a fraction of the natural noise depression, with much tighter
        // clamps so a thin sheen sits in the dip instead of rising to a pond level.
        [SerializeField, Range(0f, 1f)] private float puddleFillFraction = 0.25f;
        [SerializeField] private float puddleMinRise = 0.02f;
        [SerializeField] private float puddleMaxRise = 0.15f;
        // Cosmetic nudge added on top of the fill rise; default 0.
        [SerializeField] private float poolSurfaceLift = 0f;
        // Distance the water column extends below the bowl floor (or natural surface
        // for puddles). The cylinder's bottom hides inside the opaque mesh while the
        // top sits at the water surface, giving a visible volume of water that the
        // player traverses when descending into a deep bowl. Larger value = water
        // column reaches further past the bowl floor.
        [SerializeField] private float waterColumnExtraDepth = 0.25f;
        // Minimum column height (in world units) regardless of waterRise. Ensures
        // even shallow puddles read as a small volume of water rather than a
        // paper-thin disk.
        [SerializeField] private float waterColumnMinHeight = 0.4f;
        [SerializeField] private Color waterColor = new(0.35f, 0.65f, 0.75f, 0.43f);
        // Multiplier on waterColor.rgb when written to _EmissionColor. <1 dims the
        // self-lit glow, >1 boosts it. Tunable so the alpha-vs-emission balance can
        // be dialed without recompiling.
        [SerializeField, Range(0f, 3f)] private float waterEmissionStrength = 0.8f;

        [Header("References (auto-resolved when null)")]
        [SerializeField] private SphereWorld sphereWorld;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshCollider meshCollider;
        [SerializeField] private Transform waterPoolsParent;
        [SerializeField] private PlanetEnvironment planetEnvironment;
        [SerializeField] private string waterPoolsParentName = "Water Pools";

        // Server picks a non-zero seed on first spawn; clients receive it via initial
        // NetworkVariable replication and regenerate identical geometry.
        private readonly NetworkVariable<uint> _seed = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Vector3 _noiseOffset;
        private uint _lastGeneratedSeed;
        private bool _hasGenerated;
        private Material _waterMaterial;
        private int _lastPoolCount;
        // Min/max displacement seen across the mesh, captured during BuildMesh.
        // Used by GetNormalizedElevationAt so external systems (e.g., the rock
        // scatterer) can ask "what band is this direction?" without re-walking
        // every vertex.
        private float _minDisplacement;
        private float _maxDisplacement;

        private struct PickedPool
        {
            public Vector3 Direction;
            public float DiskRadius;
            public float WaterRise;
            public bool IsPond;
        }

        private struct CarvedBowl
        {
            public Vector3 Direction;
            public float BowlDepth;
            public float AngularRadius;     // baseline circular radius (radians)
            public float MaxAngularRadius;  // cached: AngularRadius * (1 + Irregularity), for early-out
            public float ShapeIrregularity; // 0..~0.25; ±fraction wobble of effective rim radius
            public float ShapeFrequency;    // controls lobe count around perimeter
            public Vector2 ShapeOffset;     // per-bowl noise offset so bowls don't share rim shapes
            public Vector3 TangentA;        // basis vectors for azimuth lookup
            public Vector3 TangentB;
        }

        private readonly List<PickedPool> _picked = new();
        private readonly List<CarvedBowl> _bowls = new();

        public uint CurrentSeed => _seed.Value;
        public bool HasGenerated => _hasGenerated;
        public int LastPoolCount => _lastPoolCount;
        public float BandBoundary01 => bandBoundary01;
        public float BandBoundary12 => bandBoundary12;
        public float BandBoundary23 => bandBoundary23;
        public Vector3 LaunchpadDirection =>
            launchpadDirection.sqrMagnitude > 0.0001f ? launchpadDirection.normalized : Vector3.up;

        // Normalized elevation at a unit-direction (0 = lowest mesh vertex, 1 = highest).
        // Mirrors the value the mesh's UV.y carries; lets external systems route through
        // a public API instead of caching min/max themselves.
        public float GetNormalizedElevationAt(Vector3 unitNormal)
        {
            if (!_hasGenerated) return 0.5f;
            var disp = SampleDisplacement(unitNormal);
            var range = Mathf.Max(0.0001f, _maxDisplacement - _minDisplacement);
            return Mathf.Clamp01((disp - _minDisplacement) / range);
        }

        // True if the query direction sits inside any pool's bowl footprint (or a
        // puddle's disk footprint). Used by the rock scatterer to keep rocks out of
        // ponds. Cheap: linear scan over picked pools, dot vs angular radius.
        public bool IsDirectionInsidePool(Vector3 unitNormal)
        {
            if (sphereWorld == null || _picked.Count == 0) return false;
            var R = Mathf.Max(0.01f, sphereWorld.Radius);
            var n = unitNormal.normalized;
            for (var i = 0; i < _picked.Count; i++)
            {
                var pool = _picked[i];
                var angularRadius = pool.IsPond
                    ? (pool.DiskRadius * bowlRadiusToDiskScale) / R
                    : pool.DiskRadius / R;
                if (Vector3.Dot(n, pool.Direction) >= Mathf.Cos(angularRadius)) return true;
            }
            return false;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (sphereWorld == null) sphereWorld = GetComponent<SphereWorld>();
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
            if (planetEnvironment == null) planetEnvironment = GetComponentInParent<PlanetEnvironment>();
            if (waterPoolsParent == null) waterPoolsParent = ResolveWaterPoolsParent();
        }

        private Transform ResolveWaterPoolsParent()
        {
            var envRoot = planetEnvironment != null ? planetEnvironment.transform : transform.parent;
            if (envRoot == null) return null;
            var existing = envRoot.Find(waterPoolsParentName);
            if (existing != null) return existing;
            var go = new GameObject(waterPoolsParentName);
            go.transform.SetParent(envRoot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        public override void OnNetworkSpawn()
        {
            ResolveReferences();
            _seed.OnValueChanged += HandleSeedChanged;

            if (IsServer && _seed.Value == 0)
            {
                _seed.Value = (uint)Random.Range(1, int.MaxValue);
            }

            if (_seed.Value != 0) Generate(_seed.Value);
        }

        public override void OnNetworkDespawn()
        {
            _seed.OnValueChanged -= HandleSeedChanged;
            if (sphereWorld != null) sphereWorld.ClearHeightProvider(this);
        }

        private void HandleSeedChanged(uint previous, uint current)
        {
            if (current == 0) return;
            Generate(current);
        }

        // ---------------- ISphereSurfaceHeightProvider ----------------

        public float GetHeightAt(Vector3 unitNormal)
        {
            if (!_hasGenerated) return 0f;
            return SampleDisplacement(unitNormal);
        }

        // ---------------- Generation ----------------

        private void Generate(uint seed)
        {
            if (seed == 0 || seed == _lastGeneratedSeed) return;
            _lastGeneratedSeed = seed;

            var rng = new System.Random(unchecked((int)seed));
            // Seed the noise offset so different seeds drive distinct landscapes. PerlinNoise
            // is periodic on integer coordinates, so we shift well clear of the [0, 1] range.
            _noiseOffset = new Vector3(
                (float)(rng.NextDouble() * 1000.0),
                (float)(rng.NextDouble() * 1000.0),
                (float)(rng.NextDouble() * 1000.0));

            // Pool selection uses pre-carve noise so picks reflect natural lows; we then
            // record carve params, and the mesh build picks them up via SampleDisplacement.
            _picked.Clear();
            _bowls.Clear();
            SelectAndPreparePools(rng);

            BuildMesh();
            _hasGenerated = true;

            if (sphereWorld != null) sphereWorld.SetHeightProvider(this);

            ClearWaterPools();
            if (_picked.Count > 0)
            {
                EnsureWaterMaterial();
                for (var i = 0; i < _picked.Count; i++) SpawnPoolDisk(_picked[i]);
            }
            else
            {
                // Diagnostic: if a generation pass produced zero water bodies, surface
                // it loudly so we can tell "no pools spawned" apart from "pools are
                // invisible" - the two looked identical in playtest before this log.
                Debug.LogWarning(
                    $"[PlanetTerrainGenerator] Zero pools spawned for seed {seed}. " +
                    $"Lower poolDepressionThreshold (currently {poolDepressionThreshold}) " +
                    "or shrink launchpadOuterDeg if pools keep getting filtered out.");
            }
            _lastPoolCount = _picked.Count;

            // Anchored content (launchpad, beacon, spawn points) was snapped during
            // PlanetEnvironment.OnEnable - before the height provider existed. Re-snap
            // now so they sit on the displaced surface (including any new pond bowls).
            if (planetEnvironment != null) planetEnvironment.SnapAssetsToSurface();
        }

        private void BuildMesh()
        {
            if (meshFilter == null || sphereWorld == null) return;

            var geom = IcosphereMesh.Build(subdivisions);
            var radius = sphereWorld.Radius;
            var verts = new Vector3[geom.Vertices.Length];
            var dispAtVert = new float[geom.Vertices.Length];
            var minDisp = float.PositiveInfinity;
            var maxDisp = float.NegativeInfinity;

            for (var i = 0; i < verts.Length; i++)
            {
                var n = geom.Vertices[i];
                var disp = SampleDisplacement(n);
                verts[i] = n * (radius + disp);
                dispAtVert[i] = disp;
                if (disp < minDisp) minDisp = disp;
                if (disp > maxDisp) maxDisp = disp;
            }

            // Stash extremes so GetNormalizedElevationAt can normalize ad-hoc queries
            // (rock scatterer, future band-aware systems) without rewalking the mesh.
            _minDisplacement = minDisp;
            _maxDisplacement = maxDisp;

            // UV.y in [0, 1] = normalized height. UV.x is held at 0.5 since the
            // gradient texture is a horizontal strip and the V axis carries the band.
            var range = Mathf.Max(0.0001f, maxDisp - minDisp);
            var uvs = new Vector2[verts.Length];
            for (var i = 0; i < uvs.Length; i++)
            {
                uvs[i] = new Vector2(0.5f, (dispAtVert[i] - minDisp) / range);
            }

            var mesh = new Mesh
            {
                name = "ProceduralPlanetSurface",
                indexFormat = verts.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(geom.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            if (meshCollider != null) meshCollider.sharedMesh = mesh;

            ApplyTerrainMaterial();
        }

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
                mat.SetTexture("_RockTex",  PlanetDetailTextureBaker.GetRock());
                mat.SetTexture("_DirtTex",  PlanetDetailTextureBaker.GetDirt());
                mat.SetTexture("_GrassTex", PlanetDetailTextureBaker.GetGrass());
                mat.SetTexture("_PeakTex",  PlanetDetailTextureBaker.GetPeak());
                mat.SetFloat("_TriplanarTileScale", triplanarTileScale);
                mat.SetFloat("_BandBoundary01", bandBoundary01);
                mat.SetFloat("_BandBoundary12", bandBoundary12);
                mat.SetFloat("_BandBoundary23", bandBoundary23);
                mat.SetFloat("_BandSharpness", bandSharpness);
                mat.SetFloat("_DetailStrength", detailStrength);
                mat.SetFloat("_AmbientBoost", terrainAmbientBoost);
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
