using System.Collections.Generic;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Core
{
    public class PlanetTreeSpawner : NetworkBehaviour
    {
        [SerializeField] private int treeCount = 45;
        [SerializeField] private float minTrunkHeight = 1.0f;
        [SerializeField] private float maxTrunkHeight = 2.4f;
        [SerializeField] private float minTrunkRadius = 0.07f;
        [SerializeField] private float maxTrunkRadius = 0.16f;
        [SerializeField] private float minCanopyRadius = 0.65f;
        [SerializeField] private float maxCanopyRadius = 1.55f;
        [SerializeField] private float minTreeSpacing = 2.2f;
        [SerializeField] private float launchpadExclusionDeg = 22f;
        [SerializeField] private float spawnAreaExclusionDeg = 18f;
        [SerializeField] private float trunkSinkDepth = 0.08f;

        // Synced seed — server writes a new non-zero value each Active round,
        // 0 means no trees. All clients (including late joiners) derive identical
        // placements from it.
        private readonly NetworkVariable<int> _seed = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private SphereWorld _world;
        private PlanetColorRandomizer _colorizer;
        private readonly List<GameObject> _trees = new();
        private readonly List<Material> _treeMaterials = new();
        private bool _subscribed;

        // --- Lifecycle ---

        public override void OnNetworkSpawn()
        {
            _world = GetComponent<SphereWorld>();
            _colorizer = GetComponent<PlanetColorRandomizer>();

            _seed.OnValueChanged += OnSeedChanged;

            TrySubscribeRoundManager();

            // Late-joiner: seed already set, spawn immediately
            if (_seed.Value != 0)
                SpawnTreesWithSeed(_seed.Value);
        }

        public override void OnNetworkDespawn()
        {
            _seed.OnValueChanged -= OnSeedChanged;
            UnsubscribeRoundManager();
            ClearTrees();
        }

        private void Update()
        {
            if (!IsSpawned || _subscribed) return;
            TrySubscribeRoundManager();
        }

        private void TrySubscribeRoundManager()
        {
            var rm = RoundManager.Instance;
            if (rm == null) return;
            rm.Phase.OnValueChanged += OnPhaseChanged;
            _subscribed = true;
        }

        private void UnsubscribeRoundManager()
        {
            if (!_subscribed) return;
            var rm = RoundManager.Instance;
            if (rm != null) rm.Phase.OnValueChanged -= OnPhaseChanged;
            _subscribed = false;
        }

        // --- Seed / phase callbacks ---

        private void OnPhaseChanged(RoundPhase _, RoundPhase next)
        {
            if (!IsServer) return;
            // Pick a new seed at Loading (trees ready before gameplay starts).
            // Keep seed during Active so trees persist. Clear otherwise.
            if (next == RoundPhase.Loading)
                _seed.Value = Random.Range(1, int.MaxValue);
            else if (next != RoundPhase.Active)
                _seed.Value = 0;
        }

        private void OnSeedChanged(int _, int newSeed)
        {
            if (newSeed == 0)
                ClearTrees();
            else
                SpawnTreesWithSeed(newSeed);
        }

        // --- Tree placement ---

        private void ClearTrees()
        {
            foreach (var t in _trees)
                if (t != null) Destroy(t);
            _trees.Clear();

            foreach (var material in _treeMaterials)
                if (material != null) Destroy(material);
            _treeMaterials.Clear();
        }

        private void SpawnTreesWithSeed(int seed)
        {
            ClearTrees();
            if (_world == null) return;

            // Save and restore Unity's random state so tree generation
            // doesn't affect any other system's randomness.
            var savedState = Random.state;
            Random.InitState(seed);

            var exclusions = BuildExclusionList();
            ResolveColors(out var trunkColor, out var leafColor);
            var trunkMat = MakeOpaqueMat(trunkColor);
            var leafMat  = MakeOpaqueMat(leafColor);
            _treeMaterials.Add(trunkMat);
            _treeMaterials.Add(leafMat);

            var placedDirs = new List<Vector3>(treeCount);
            var attempts   = 0;
            var maxAttempts = treeCount * 10;

            while (_trees.Count < treeCount && attempts < maxAttempts)
            {
                attempts++;
                var up = Random.onUnitSphere;

                if (IsExcluded(up, exclusions)) continue;
                if (IsTooClose(up, placedDirs))  continue;

                var surfacePos = _world.GetSurfacePoint(up, 0f);
                var rot    = Quaternion.FromToRotation(Vector3.up, up);
                var trunkH = Random.Range(minTrunkHeight, maxTrunkHeight);
                var trunkR = Random.Range(minTrunkRadius, maxTrunkRadius);
                var canopyR = Random.Range(minCanopyRadius, maxCanopyRadius);

                var treeRoot = new GameObject("Tree");
                treeRoot.transform.SetParent(transform, true);

                PlaceTrunk(treeRoot.transform, surfacePos, up, rot, trunkH, trunkR, trunkSinkDepth, trunkMat);
                PlaceCanopy(treeRoot.transform, surfacePos, up, rot, trunkH, canopyR, leafMat);

                placedDirs.Add(up);
                _trees.Add(treeRoot);
            }

            Random.state = savedState;
        }

        // --- Exclusion ---

        private readonly struct ExclusionZone
        {
            public readonly Vector3 Dir;
            public readonly float AngleDeg;
            public ExclusionZone(Vector3 d, float a) { Dir = d; AngleDeg = a; }
        }

        private List<ExclusionZone> BuildExclusionList()
        {
            var list = new List<ExclusionZone>();
            var planetRoot = ResolvePlanetRoot();

            // Per-planet lookups instead of global GameObject.Find: in multi-planet builds
            // Find returns whichever active planet Unity reaches first, so two clients with
            // the same seed could otherwise place trees against different exclusion zones.
            var launchpadTransform = ResolveLaunchpadTransform(planetRoot);
            if (launchpadTransform != null)
                list.Add(new ExclusionZone(_world.GetUp(launchpadTransform.position), launchpadExclusionDeg));

            var crashTransform = ResolveCrashTransform(planetRoot);
            if (crashTransform != null)
                list.Add(new ExclusionZone(_world.GetUp(crashTransform.position), spawnAreaExclusionDeg));

            return list;
        }

        private Transform ResolvePlanetRoot()
        {
            var env = GetComponentInParent<PlanetEnvironment>(true);
            return env != null ? env.transform : transform.root;
        }

        private Transform ResolveLaunchpadTransform(Transform planetRoot)
        {
            if (planetRoot != null)
            {
                var env = planetRoot.GetComponent<PlanetEnvironment>();
                if (env != null && env.LaunchpadZone != null) return env.LaunchpadZone.transform;
                var local = planetRoot.GetComponentInChildren<LaunchpadZone>(true);
                if (local != null) return local.transform;
            }
            var global = GameObject.Find("Part Launchpad");
            return global != null ? global.transform : null;
        }

        private static Transform ResolveCrashTransform(Transform planetRoot)
        {
            if (planetRoot != null)
            {
                var local = FindByName(planetRoot, "Crash Dirt Patch");
                if (local != null) return local;
            }
            var global = GameObject.Find("Crash Dirt Patch");
            return global != null ? global.transform : null;
        }

        private static Transform FindByName(Transform root, string targetName)
        {
            if (root == null) return null;
            if (root.name == targetName) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindByName(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        private static bool IsExcluded(Vector3 up, List<ExclusionZone> zones)
        {
            foreach (var z in zones)
                if (Vector3.Angle(up, z.Dir) < z.AngleDeg)
                    return true;
            return false;
        }

        private bool IsTooClose(Vector3 up, List<Vector3> placed)
        {
            var angleDeg = minTreeSpacing / _world.Radius * Mathf.Rad2Deg;
            foreach (var p in placed)
                if (Vector3.Angle(up, p) < angleDeg)
                    return true;
            return false;
        }

        // --- Mesh builders ---

        private static void PlaceTrunk(Transform parent, Vector3 surface, Vector3 up, Quaternion rot,
            float height, float radius, float sinkDepth, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(parent, true);
            go.transform.position = surface + up * (height * 0.5f - Mathf.Max(0f, sinkDepth));
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(radius, height * 0.5f, radius);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private static void PlaceCanopy(Transform parent, Vector3 surface, Vector3 up, Quaternion rot,
            float trunkHeight, float radius, Material mat)
        {
            PlaceCanopySphere(parent, surface + up * (trunkHeight + radius * 0.8f), radius, mat);

            var tangent = Vector3.Cross(up, Random.onUnitSphere).normalized;
            var offset  = tangent * radius * Random.Range(0.3f, 0.55f)
                        + up      * radius * Random.Range(-0.3f, 0.2f);
            PlaceCanopySphere(parent,
                surface + up * (trunkHeight + radius * 0.65f) + offset,
                radius * Random.Range(0.55f, 0.75f), mat);
        }

        private static void PlaceCanopySphere(Transform parent, Vector3 pos, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * radius * 2f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // --- Material / color helpers ---

        private void ResolveColors(out Color trunkColor, out Color leafColor)
        {
            if (_colorizer == null)
            {
                trunkColor = new Color(0.28f, 0.16f, 0.05f);
                leafColor  = new Color(0.18f, 0.52f, 0.12f);
                return;
            }

            var south = _colorizer.SouthColor;
            var north = _colorizer.NorthColor;

            Color.RGBToHSV(south, out _, out _, out var vS);
            Color.RGBToHSV(north, out _, out _, out var vN);

            var darker  = vS <= vN ? south : north;
            var lighter = vS <= vN ? north : south;

            Color.RGBToHSV(darker,  out var hD, out var sD, out var vD);
            trunkColor = Color.HSVToRGB(hD, Mathf.Clamp01(sD * 0.65f), Mathf.Clamp01(vD * 0.55f));

            Color.RGBToHSV(lighter, out var hL, out var sL, out var vL);
            leafColor = Color.HSVToRGB(hL, Mathf.Clamp01(sL * 0.9f), Mathf.Clamp01(vL * 1.15f));
        }

        private static Material MakeOpaqueMat(Color color)
        {
            var isUrp  = GraphicsSettings.currentRenderPipeline != null;
            var shader = isUrp
                ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit"))
                : Shader.Find("Standard");

            if (shader == null)
            {
                var fb = new Material(Shader.Find("Diffuse") ?? Shader.Find("Legacy Shaders/Diffuse"));
                fb.color = color;
                return fb;
            }

            var mat = new Material(shader);
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Metallic",   0f);
            if (isUrp) mat.SetColor("_BaseColor", color);
            else       mat.color = color;
            return mat;
        }
    }
}
