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
    public partial class PlanetTerrainGenerator : NetworkBehaviour, ISphereSurfaceHeightProvider
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
        // Only affects bands whose ColorBlend is 0 (procedural grayscale path).
        [SerializeField, Range(0f, 1f)] private float detailStrength = 0.5f;
        // Constant ambient added on top of SH so dark sides aren't pitch black.
        [SerializeField, Range(0f, 1f)] private float terrainAmbientBoost = 0.25f;
        // Per-band color blend: 0 = use the elevation gradient modulated by texture
        // luminance (good for procedural grayscale detail). 1 = use the texture's
        // RGB directly as the band albedo (good for full-color authored textures
        // like grass/dirt). Defaults: rock/peak procedural, dirt/grass full color.
        [SerializeField, Range(0f, 1f)] private float rockColorBlend = 0f;
        [SerializeField, Range(0f, 1f)] private float dirtColorBlend = 1f;
        [SerializeField, Range(0f, 1f)] private float grassColorBlend = 1f;
        [SerializeField, Range(0f, 1f)] private float peakColorBlend = 0f;
        // Per-band texture overrides. When null, the procedural baker fills the slot.
        // When assigned, the authored texture replaces the procedural one for that
        // band - usually paired with ColorBlend = 1 so the authored RGB drives albedo.
        [SerializeField] private Texture2D rockColorTexture;
        [SerializeField] private Texture2D dirtColorTexture;
        [SerializeField] private Texture2D grassColorTexture;
        [SerializeField] private Texture2D peakColorTexture;

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

    }
}
