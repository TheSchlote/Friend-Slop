#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Round;
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

            EnsureTier1Environment(tier1Def);
            BuildExtraPlanet(tier2Def, "Tier 2 Planet", new Vector3(Tier2OffsetX, 0f, 0f), Tier2Radius, new Color(0.55f, 0.42f, 0.35f), new Color(0.32f, 0.24f, 0.2f));
            BuildExtraPlanet(tier3Def, "Tier 3 Planet", new Vector3(Tier3OffsetX, 0f, 0f), Tier3Radius, new Color(0.45f, 0.32f, 0.65f), new Color(0.85f, 0.72f, 0.92f));

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
                "RoundManager prefab is wired with the catalog, starting planet, and default objective.",
                "OK");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(PlanetsFolder))
                AssetDatabase.CreateFolder("Assets", "Planets");
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
            if (contentRootProp != null)
            {
                var tinyWorld = GameObject.Find("Tiny Sphere World");
                if (tinyWorld != null) contentRootProp.objectReferenceValue = tinyWorld;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(env);
        }

        private static PlanetEnvironment FindEnvironmentForName(string objectName)
        {
            var go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<PlanetEnvironment>() : null;
        }

        private static void BuildExtraPlanet(PlanetDefinition planet, string objectName, Vector3 worldPosition, float radius, Color groundColor, Color padColor)
        {
            // Idempotency: skip rebuilding if the user already has one of these in scene.
            var existing = GameObject.Find(objectName);
            if (existing != null)
            {
                var existingEnv = existing.GetComponent<PlanetEnvironment>();
                if (existingEnv != null && existingEnv.Planet == planet) return;
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
            beacon.transform.position = world.GetSurfacePoint(padNormal, 4.5f);
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
            EditorUtility.SetDirty(env);

            // Tier 2+ planets start inactive — they are enabled on-demand when players travel to them.
            root.SetActive(false);
        }

        private static void WireRoundManagerPrefab(PlanetCatalog catalog, PlanetDefinition startingPlanet, RoundObjective defaultObjective)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"RoundManager prefab not found at {RoundManagerPrefabPath}; skipping catalog wiring.");
                return;
            }
            var round = prefab.GetComponent<RoundManager>();
            if (round == null) return;

            var so = new SerializedObject(round);
            so.FindProperty("planetCatalog").objectReferenceValue = catalog;
            so.FindProperty("startingPlanet").objectReferenceValue = startingPlanet;
            so.FindProperty("defaultObjective").objectReferenceValue = defaultObjective;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
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
    }
}
#endif
