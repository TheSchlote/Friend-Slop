#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Loot;
using FriendSlop.Round;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static class FriendSlopMultiPlanetBuilder
    {
        private const string PlanetsFolder = "Assets/Planets";
        private const string CatalogPath = "Assets/Planets/PlanetCatalog.asset";
        private const string DefaultObjectivePath = "Assets/Planets/DefaultRocketAssemblyObjective.asset";
        private const string Tier1AssetPath = "Assets/Planets/Tier1_StarterJunk.asset";
        private const string Tier2AssetPath = "Assets/Planets/Tier2_RustyMoon.asset";
        private const string Tier3AssetPath = "Assets/Planets/Tier3_VioletGiant.asset";
        private const string Tier2ObjectivePath = "Assets/Planets/Tier2_SmashAndGrab.asset";
        private const string Tier3ObjectivePath = "Assets/Planets/Tier3_HoldThePad.asset";
        private const string RoundManagerPrefabPath = "Assets/Prefabs/RoundManager.prefab";

        private const string PoolLootFolder = "Assets/Prefabs/PoolLoot";
        private const string LootMaterialsFolder = "Assets/Materials/LootRarity";
        private const string LootPoolsFolder = "Assets/Loot";
        private const string Tier2LootPoolPath = "Assets/Loot/Tier2_LootPool.asset";
        private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        private const int Tier2QuotaTarget = 350;
        private const float Tier2RoundLength = 240f;
        private const float Tier3SurvivalSeconds = 180f;

        private const float Tier1Radius = 36f;
        private const float Tier2Radius = 26f;
        private const float Tier3Radius = 48f;
        private const float Tier2OffsetX = 320f;
        private const float Tier3OffsetX = -360f;

        [MenuItem("Tools/Friend Slop/Build Tier 2 + Tier 3 Planets")]
        public static void BuildExtraPlanets()
        {
            EnsureFolders();

            var defaultObjective = LoadOrCreate<RocketAssemblyObjective>(DefaultObjectivePath);
            var smashAndGrab = CreateOrUpdateQuotaObjective(
                Tier2ObjectivePath,
                title: "Smash and Grab",
                description: "Scrape together the quota in scrap value before the moon's purge cycle. Reach the launchpad before the timer hits zero.",
                quotaOverride: Tier2QuotaTarget,
                requireBoarding: true,
                failOnTimerExpired: true);
            var holdThePad = CreateOrUpdateSurvivalObjective(
                Tier3ObjectivePath,
                title: "Hold the Pad",
                description: "Hold the launchpad while the violet wildlife stirs. When the extraction beam fires, anyone off the pad is left behind.",
                survivalSeconds: Tier3SurvivalSeconds,
                requireBoardingOnSurvive: true);

            var tier1Def = LoadOrCreatePlanetDefinition(Tier1AssetPath, "Starter Junk", 1,
                "Original prototype planet — assemble the rocket from scattered parts and launch with the crew.");
            var tier2Def = LoadOrCreatePlanetDefinition(Tier2AssetPath, "Rusty Moon", 2,
                "Cramped moon. Smash-and-grab loot run with a hard countdown.");
            var tier3Def = LoadOrCreatePlanetDefinition(Tier3AssetPath, "Violet Giant", 3,
                "Sprawling violet planet. Hold the launchpad until the extraction beam fires.");

            EnsurePlanetConfig(tier2Def, smashAndGrab, Tier2RoundLength);
            EnsurePlanetConfig(tier3Def, holdThePad, Tier3SurvivalSeconds);

            var catalog = LoadOrCreate<PlanetCatalog>(CatalogPath);
            AssignCatalog(catalog, new[] { tier1Def, tier2Def, tier3Def });

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("Open the Friend Slop prototype scene before running the multi-planet builder.");
                return;
            }

            var testLootPrefabs = BuildTestLootPrefabs();
            var tier2LootPool = BuildTier2LootPool(testLootPrefabs);
            EnsureNetworkPrefabsRegistered(testLootPrefabs);

            EnsureTier1Environment(tier1Def);
            BuildExtraPlanet(tier2Def, "Tier 2 Planet", new Vector3(Tier2OffsetX, 0f, 0f), Tier2Radius, new Color(0.55f, 0.42f, 0.35f), new Color(0.32f, 0.24f, 0.2f), tier2LootPool);
            BuildExtraPlanet(tier3Def, "Tier 3 Planet", new Vector3(Tier3OffsetX, 0f, 0f), Tier3Radius, new Color(0.45f, 0.32f, 0.65f), new Color(0.85f, 0.72f, 0.92f), null);

            WireRoundManagerPrefab(catalog, tier1Def, defaultObjective);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Friend Slop",
                "Tier 2 and Tier 3 planets added.\n\n" +
                "Assets under Assets/Planets/:\n" +
                "  - PlanetCatalog + tier 1/2/3 PlanetDefinitions\n" +
                "  - DefaultRocketAssemblyObjective (tier 1 fallback)\n" +
                "  - Tier2_SmashAndGrab (Quota, $350 / 240s)\n" +
                "  - Tier3_HoldThePad (Survival, 180s)\n\n" +
                $"Loot pool: {Tier2LootPoolPath}\n" +
                $"Test loot prefabs under {PoolLootFolder}/\n" +
                "Tier 2 spawns rolled loot (8 spawn points) when players travel there.\n\n" +
                "RoundManager prefab is wired with the catalog, starting planet, and default objective.",
                "OK");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(PlanetsFolder))
                AssetDatabase.CreateFolder("Assets", "Planets");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PoolLootFolder))
                AssetDatabase.CreateFolder("Assets/Prefabs", "PoolLoot");
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(LootMaterialsFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "LootRarity");
            if (!AssetDatabase.IsValidFolder(LootPoolsFolder))
                AssetDatabase.CreateFolder("Assets", "Loot");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static PlanetDefinition LoadOrCreatePlanetDefinition(string path, string displayName, int tier, string description)
        {
            var def = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<PlanetDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }

            var so = new SerializedObject(def);
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("tier").intValue = tier;
            var descProp = so.FindProperty("description");
            if (string.IsNullOrEmpty(descProp.stringValue))
                descProp.stringValue = description;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        private static QuotaObjective CreateOrUpdateQuotaObjective(string path, string title, string description,
            int quotaOverride, bool requireBoarding, bool failOnTimerExpired)
        {
            var obj = AssetDatabase.LoadAssetAtPath<QuotaObjective>(path);
            if (obj == null)
            {
                obj = ScriptableObject.CreateInstance<QuotaObjective>();
                AssetDatabase.CreateAsset(obj, path);
            }

            var so = new SerializedObject(obj);
            so.FindProperty("title").stringValue = title;
            var descProp = so.FindProperty("description");
            if (string.IsNullOrEmpty(descProp.stringValue)) descProp.stringValue = description;
            so.FindProperty("quotaOverride").intValue = quotaOverride;
            so.FindProperty("requireBoarding").boolValue = requireBoarding;
            so.FindProperty("failOnTimerExpired").boolValue = failOnTimerExpired;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
            return obj;
        }

        private static SurvivalObjective CreateOrUpdateSurvivalObjective(string path, string title, string description,
            float survivalSeconds, bool requireBoardingOnSurvive)
        {
            var obj = AssetDatabase.LoadAssetAtPath<SurvivalObjective>(path);
            if (obj == null)
            {
                obj = ScriptableObject.CreateInstance<SurvivalObjective>();
                AssetDatabase.CreateAsset(obj, path);
            }

            var so = new SerializedObject(obj);
            so.FindProperty("title").stringValue = title;
            var descProp = so.FindProperty("description");
            if (string.IsNullOrEmpty(descProp.stringValue)) descProp.stringValue = description;
            so.FindProperty("survivalSeconds").floatValue = survivalSeconds;
            so.FindProperty("requireBoardingOnSurvive").boolValue = requireBoardingOnSurvive;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
            return obj;
        }

        private static void EnsurePlanetConfig(PlanetDefinition planet, RoundObjective objective, float roundLengthOverride)
        {
            if (planet == null) return;
            var so = new SerializedObject(planet);
            var objectiveProp = so.FindProperty("objective");
            if (objectiveProp.objectReferenceValue == null)
                objectiveProp.objectReferenceValue = objective;

            var lengthProp = so.FindProperty("roundLengthOverride");
            if (Mathf.Approximately(lengthProp.floatValue, 0f))
                lengthProp.floatValue = roundLengthOverride;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(planet);
        }

        private static void AssignCatalog(PlanetCatalog catalog, PlanetDefinition[] planets)
        {
            var so = new SerializedObject(catalog);
            var list = so.FindProperty("planets");
            list.arraySize = planets.Length;
            for (var i = 0; i < planets.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = planets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void EnsureTier1Environment(PlanetDefinition tier1)
        {
            // Tier 1 was authored by FriendSlopSceneBuilder. Locate its launchpad zone
            // and the four player spawn GameObjects, then wrap them in a PlanetEnvironment
            // so the round manager can resolve them by PlanetDefinition.
            var existing = FindEnvironmentForName("Tier 1 Planet");
            if (existing != null)
            {
                ConfigureExistingTier1(existing, tier1);
                return;
            }

            var root = new GameObject("Tier 1 Planet");
            var env = root.AddComponent<PlanetEnvironment>();
            ConfigureExistingTier1(env, tier1);
        }

        private static void ConfigureExistingTier1(PlanetEnvironment env, PlanetDefinition tier1)
        {
            var partLaunchpad = GameObject.Find("Part Launchpad");
            var zone = partLaunchpad != null ? partLaunchpad.GetComponent<LaunchpadZone>() : null;

            var spawns = new List<Transform>();
            for (var i = 1; i <= 4; i++)
            {
                var spawn = GameObject.Find($"Player Spawn {i}");
                if (spawn != null) spawns.Add(spawn.transform);
            }

            env.Configure(tier1, zone, spawns.ToArray());

            // Wire the actual visual root of Tier 1 ("Tiny Sphere World" is the legacy scene
            // root that predates the PlanetEnvironment wrapper). Without this reference,
            // disabling the wrapper leaves the sphere and its shadow visible during transitions.
            var so = new SerializedObject(env);
            var contentRootProp = so.FindProperty("contentRoot");
            var tinyWorld = GameObject.Find("Tiny Sphere World");
            if (contentRootProp != null && tinyWorld != null)
                contentRootProp.objectReferenceValue = tinyWorld;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Tier 1's SphereWorld lives on the legacy content root, not under the env itself.
            var sphere = tinyWorld != null ? tinyWorld.GetComponentInChildren<SphereWorld>(true) : null;
            if (sphere != null) env.SetSphereWorld(sphere);

            env.SnapAssetsToSurface();
            EditorUtility.SetDirty(env);
        }

        private static PlanetEnvironment FindEnvironmentForName(string objectName)
        {
            var go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<PlanetEnvironment>() : null;
        }

        private static void BuildExtraPlanet(PlanetDefinition planet, string objectName, Vector3 worldPosition, float radius, Color groundColor, Color padColor, LootPool lootPool)
        {
            // Idempotency: if the planet is already in the scene, just (re)wire its loot
            // spawner so the pool reference stays current, then exit.
            var existing = GameObject.Find(objectName);
            if (existing != null)
            {
                var existingEnv = existing.GetComponent<PlanetEnvironment>();
                if (existingEnv != null && existingEnv.Planet == planet)
                {
                    if (lootPool != null)
                        EnsurePlanetLootSpawner(existing, existingEnv, lootPool);
                    return;
                }
            }

            var root = new GameObject(objectName);
            root.transform.position = worldPosition;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"{objectName} Sphere";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * radius * 2f;
            ApplyColorMaterial(sphere, groundColor);
            var world = sphere.AddComponent<SphereWorld>();
            var worldSo = new SerializedObject(world);
            worldSo.FindProperty("radius").floatValue = radius;
            worldSo.FindProperty("gravityAcceleration").floatValue = 18f;
            worldSo.ApplyModifiedPropertiesWithoutUndo();

            // Launchpad assembly: pad cylinder + LaunchpadZone, parked on the planet's "north".
            var padNormal = Vector3.up;
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = $"{objectName} Launchpad";
            pad.transform.SetParent(root.transform, false);
            PlaceOnSurface(world, pad.transform, padNormal, 0.12f);
            pad.transform.localScale = new Vector3(4.4f, 0.08f, 4.4f);
            ApplyColorMaterial(pad, padColor);
            DestroyComponent(pad.GetComponent<Collider>());
            var zone = pad.AddComponent<LaunchpadZone>();

            // Beacon so players can see the pad from a distance.
            var beacon = new GameObject($"{objectName} Beacon");
            beacon.transform.SetParent(root.transform, false);
            const float beaconSurfaceOffset = 4.5f;
            beacon.transform.position = world.GetSurfacePoint(padNormal, beaconSurfaceOffset);
            var beaconAnchor = beacon.AddComponent<PlanetSurfaceAnchor>();
            beaconAnchor.SetSurfaceOffset(beaconSurfaceOffset);
            beaconAnchor.SetAlignRotation(false);
            var beaconLight = beacon.AddComponent<Light>();
            beaconLight.type = LightType.Point;
            beaconLight.color = padColor * 1.4f;
            beaconLight.range = 18f;
            beaconLight.intensity = 2.6f;

            // Four spawn anchors arranged around the launchpad.
            var spawnRoot = new GameObject($"{objectName} Spawn Points");
            spawnRoot.transform.SetParent(root.transform, false);
            var spawns = new Transform[4];
            var spawnNormals = new[]
            {
                new Vector3(-0.18f, 0.97f, -0.16f),
                new Vector3(0.18f, 0.97f, -0.16f),
                new Vector3(-0.18f, 0.97f, 0.16f),
                new Vector3(0.18f, 0.97f, 0.16f)
            };
            for (var i = 0; i < spawns.Length; i++)
            {
                var spawn = new GameObject($"{objectName} Spawn {i + 1}");
                spawn.transform.SetParent(spawnRoot.transform, false);
                var normal = spawnNormals[i].normalized;
                spawn.transform.position = world.GetSurfacePoint(normal, 0.25f);
                spawn.transform.rotation = world.GetSurfaceRotation(normal, Vector3.forward);
                spawns[i] = spawn.transform;
            }

            var env = root.AddComponent<PlanetEnvironment>();
            env.Configure(planet, zone, spawns);
            env.SetSphereWorld(world);
            env.SnapAssetsToSurface();
            EditorUtility.SetDirty(env);

            if (lootPool != null)
                EnsurePlanetLootSpawner(root, env, lootPool);

            // Tier 2+ planets start inactive — they are enabled on-demand when players travel to them.
            root.SetActive(false);
        }

        private static void EnsurePlanetLootSpawner(GameObject planetRoot, PlanetEnvironment env, LootPool lootPool)
        {
            var spawner = planetRoot.GetComponentInChildren<PlanetLootSpawner>(true);
            var world = env.SphereWorld != null ? env.SphereWorld : planetRoot.GetComponentInChildren<SphereWorld>(true);
            if (world == null) return;

            // Eight loot anchors arranged around the planet's "northern hemisphere", away
            // from the launchpad so players have a reason to explore.
            var anchors = new[]
            {
                new Vector3( 0.55f, 0.78f,  0.30f),
                new Vector3(-0.55f, 0.78f,  0.30f),
                new Vector3( 0.55f, 0.78f, -0.30f),
                new Vector3(-0.55f, 0.78f, -0.30f),
                new Vector3( 0.30f, 0.50f,  0.81f),
                new Vector3(-0.30f, 0.50f,  0.81f),
                new Vector3( 0.30f, 0.50f, -0.81f),
                new Vector3(-0.30f, 0.50f, -0.81f),
            };

            var spawnRoot = planetRoot.transform.Find($"{planetRoot.name} Loot Spawn Points");
            if (spawnRoot == null)
            {
                var go = new GameObject($"{planetRoot.name} Loot Spawn Points");
                go.transform.SetParent(planetRoot.transform, false);
                spawnRoot = go.transform;
            }

            var points = new Transform[anchors.Length];
            for (var i = 0; i < anchors.Length; i++)
            {
                var name = $"{planetRoot.name} Loot Spawn {i + 1}";
                var existingChild = spawnRoot.Find(name);
                var go = existingChild != null ? existingChild.gameObject : new GameObject(name);
                if (existingChild == null) go.transform.SetParent(spawnRoot, false);

                var normal = anchors[i].normalized;
                go.transform.position = world.GetSurfacePoint(normal, 0.4f);
                go.transform.rotation = world.GetSurfaceRotation(normal, Vector3.forward);
                points[i] = go.transform;
                EditorUtility.SetDirty(go);
            }

            if (spawner == null)
                spawner = planetRoot.AddComponent<PlanetLootSpawner>();
            spawner.Configure(lootPool, points, rolls: 1);
            EditorUtility.SetDirty(spawner);
        }

        [MenuItem("Tools/Friend Slop/Snap Planet Assets to Surface")]
        public static void SnapAllPlanetAssetsToSurface()
        {
            var envs = Object.FindObjectsByType<PlanetEnvironment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (envs.Length == 0)
            {
                Debug.LogWarning("Friend Slop: no PlanetEnvironment found in the active scene.");
                return;
            }

            Undo.SetCurrentGroupName("Snap planet assets to surface");
            var undoGroup = Undo.GetCurrentGroup();

            foreach (var env in envs)
            {
                if (env == null) continue;

                // Auto-wire the sphere world reference so future runs (and OnEnable snaps)
                // don't have to scan the hierarchy.
                if (env.SphereWorld == null)
                {
                    var found = env.GetComponentInChildren<SphereWorld>(true);
                    if (found == null && env.ContentRoot != null)
                        found = env.ContentRoot.GetComponentInChildren<SphereWorld>(true);
                    if (found != null)
                    {
                        Undo.RecordObject(env, "Wire planet sphere world");
                        env.SetSphereWorld(found);
                    }
                }

                if (env.LaunchpadZone != null)
                    Undo.RecordObject(env.LaunchpadZone.transform, "Snap launchpad");
                if (env.PlayerSpawnPoints != null)
                {
                    foreach (var spawn in env.PlayerSpawnPoints)
                        if (spawn != null) Undo.RecordObject(spawn, "Snap player spawn");
                }
                foreach (var anchor in env.GetComponentsInChildren<PlanetSurfaceAnchor>(true))
                    if (anchor != null) Undo.RecordObject(anchor.transform, "Snap surface anchor");

                env.SnapAssetsToSurface();
                EditorUtility.SetDirty(env);
            }

            Undo.CollapseUndoOperations(undoGroup);

            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"Friend Slop: snapped {envs.Length} planet environment(s) to their surface.");
        }

        private static void WireRoundManagerPrefab(PlanetCatalog catalog, PlanetDefinition startingPlanet, RoundObjective defaultObjective)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"RoundManager prefab not found at {RoundManagerPrefabPath}; skipping catalog wiring.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(RoundManagerPrefabPath);
            try
            {
                var round = root.GetComponent<RoundManager>();
                if (round == null) return;

                var orchestrator = root.GetComponent<PlanetSceneOrchestrator>();
                if (orchestrator == null)
                    orchestrator = root.AddComponent<PlanetSceneOrchestrator>();

                var so = new SerializedObject(round);
                so.FindProperty("planetCatalog").objectReferenceValue = catalog;
                so.FindProperty("startingPlanet").objectReferenceValue = startingPlanet;
                so.FindProperty("planetSceneOrchestrator").objectReferenceValue = orchestrator;
                so.FindProperty("defaultObjective").objectReferenceValue = defaultObjective;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RoundManagerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void PlaceOnSurface(SphereWorld world, Transform target, Vector3 normal, float heightOffset)
        {
            normal = normal.normalized;
            target.position = world.GetSurfacePoint(normal, heightOffset);
            target.rotation = world.GetSurfaceRotation(normal, Vector3.forward);
        }

        private static void ApplyColorMaterial(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color, name = $"{go.name} Material" };
            renderer.sharedMaterial = material;
        }

        private static void DestroyComponent(Component component)
        {
            if (component != null) Object.DestroyImmediate(component);
        }

        // ----- Loot pool system -----

        private readonly struct TestLootSpec
        {
            public readonly string Name;
            public readonly int Value;
            public readonly LootRarity Rarity;
            public readonly PrimitiveType Shape;
            public readonly Vector3 Scale;
            public readonly float CarrySpeedMultiplier;

            public TestLootSpec(string name, int value, LootRarity rarity, PrimitiveType shape, Vector3 scale, float carrySpeedMultiplier)
            {
                Name = name;
                Value = value;
                Rarity = rarity;
                Shape = shape;
                Scale = scale;
                CarrySpeedMultiplier = carrySpeedMultiplier;
            }
        }

        private static TestLootSpec[] GetTestLootSpecs() => new[]
        {
            // Values scale roughly 4x per tier so picking up rarer loot is dramatically better.
            new TestLootSpec("Cracked Bolt",      25,   LootRarity.Common,    PrimitiveType.Cube,     new Vector3(0.55f, 0.55f, 0.55f), 0.96f),
            new TestLootSpec("Rusty Toolbox",     80,   LootRarity.Uncommon,  PrimitiveType.Cube,     new Vector3(1.1f, 0.55f, 0.7f),  0.86f),
            new TestLootSpec("Holo Tablet",       240,  LootRarity.Rare,      PrimitiveType.Cube,     new Vector3(0.95f, 0.08f, 0.65f), 0.9f),
            new TestLootSpec("Cryo Diamond",      650,  LootRarity.Epic,      PrimitiveType.Sphere,   new Vector3(0.7f, 0.7f, 0.7f),   0.78f),
            new TestLootSpec("Quantum Idol",      1500, LootRarity.Legendary, PrimitiveType.Capsule,  new Vector3(0.7f, 1.1f, 0.7f),   0.7f),
        };

        private static NetworkLootItem[] BuildTestLootPrefabs()
        {
            var specs = GetTestLootSpecs();
            var prefabs = new NetworkLootItem[specs.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                prefabs[i] = BuildSingleTestLootPrefab(specs[i]);
            }
            return prefabs;
        }

        private static NetworkLootItem BuildSingleTestLootPrefab(TestLootSpec spec)
        {
            var prefabPath = $"{PoolLootFolder}/{SanitizeAssetName(spec.Name)}.prefab";
            var lootObject = GameObject.CreatePrimitive(spec.Shape);
            lootObject.name = spec.Name;
            lootObject.transform.localScale = spec.Scale;
            ApplyRarityMaterial(lootObject, spec.Rarity);

            var body = lootObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Lerp(1.2f, 8f, 1f - spec.CarrySpeedMultiplier);
            body.angularDamping = 0.15f;
            body.useGravity = false;

            lootObject.AddComponent<SphericalRigidbodyGravity>();
            lootObject.AddComponent<NetworkObject>();
            lootObject.AddComponent<NetworkTransform>();
            var loot = lootObject.AddComponent<NetworkLootItem>();
            var so = new SerializedObject(loot);
            so.FindProperty("itemName").stringValue = spec.Name;
            so.FindProperty("value").intValue = spec.Value;
            so.FindProperty("carrySpeedMultiplier").floatValue = spec.CarrySpeedMultiplier;
            so.FindProperty("carryDistance").floatValue = Mathf.Lerp(2.35f, 1.7f, 1f - spec.CarrySpeedMultiplier);
            so.FindProperty("shipPartType").enumValueIndex = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(lootObject, prefabPath);
            Object.DestroyImmediate(lootObject);
            return prefab.GetComponent<NetworkLootItem>();
        }

        private static void ApplyRarityMaterial(GameObject go, LootRarity rarity)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var path = $"{LootMaterialsFolder}/Rarity_{rarity}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = $"Rarity_{rarity}" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = rarity.DisplayTint();
            // Glow for higher rarities so they're visible on the planet at a distance.
            if (rarity >= LootRarity.Rare && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", rarity.DisplayTint() * (rarity == LootRarity.Legendary ? 1.6f : 1.0f));
            }
            EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material;
        }

        private static LootPool BuildTier2LootPool(NetworkLootItem[] testLoot)
        {
            var pool = AssetDatabase.LoadAssetAtPath<LootPool>(Tier2LootPoolPath);
            if (pool == null)
            {
                pool = ScriptableObject.CreateInstance<LootPool>();
                AssetDatabase.CreateAsset(pool, Tier2LootPoolPath);
            }

            var entries = new List<LootPool.Entry>();
            var specs = GetTestLootSpecs();
            for (var i = 0; i < testLoot.Length && i < specs.Length; i++)
            {
                if (testLoot[i] == null) continue;
                entries.Add(new LootPool.Entry
                {
                    prefab = testLoot[i],
                    rarity = specs[i].Rarity,
                });
            }
            pool.SetEntries(entries);
            EditorUtility.SetDirty(pool);
            return pool;
        }

        private static void EnsureNetworkPrefabsRegistered(NetworkLootItem[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null)
            {
                Debug.LogWarning($"NetworkPrefabsList not found at {NetworkPrefabsListPath}; rebuild the prototype scene first.");
                return;
            }

            var so = new SerializedObject(list);
            var listProp = so.FindProperty("List");

            foreach (var loot in prefabs)
            {
                if (loot == null) continue;
                if (ContainsPrefab(listProp, loot.gameObject)) continue;

                var index = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(index);
                var entry = listProp.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Override").enumValueIndex = (int)NetworkPrefabOverride.None;
                entry.FindPropertyRelative("Prefab").objectReferenceValue = loot.gameObject;
                entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
                entry.FindPropertyRelative("SourceHashToOverride").uintValue = 0;
                entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(list);
        }

        private static bool ContainsPrefab(SerializedProperty listProp, GameObject prefab)
        {
            for (var i = 0; i < listProp.arraySize; i++)
            {
                var entry = listProp.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Prefab").objectReferenceValue == prefab) return true;
            }
            return false;
        }

        private static string SanitizeAssetName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }
}
#endif
