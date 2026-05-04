#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FriendSlop.Core;
using FriendSlop.Loot;
using FriendSlop.Networking;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using FriendSlop.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static class FriendSlopSceneWiringRepair
    {
        private const string PrototypeScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
        private const string ShipInteriorScenePath = "Assets/Scenes/ShipInterior.unity";
        private const string StarterJunkScenePath = "Assets/Scenes/Planet_StarterJunk.unity";
        private const string RustyMoonScenePath = "Assets/Scenes/Planet_RustyMoon.unity";
        private const string VioletGiantScenePath = "Assets/Scenes/Planet_VioletGiant.unity";
        private const string SceneCatalogPath = "Assets/SceneDefinitions/MainGameSceneCatalog.asset";
        private const string ShipInteriorSceneDefinitionPath = "Assets/SceneDefinitions/ShipInterior_Scene.asset";
        private const string VioletGiantSceneDefinitionPath = "Assets/SceneDefinitions/Planet_VioletGiant_Scene.asset";
        private const string VioletGiantPlanetPath = "Assets/Planets/Tier3_VioletGiant.asset";
        private const string ShipInteriorRootName = "Bigger-On-The-Inside Ship Interior";
        private static readonly Vector3 ShipInteriorRootPosition = new(0f, 5000f, 0f);

        private static readonly Vector3[] RustyMoonMonsterNormals =
        {
            new(0.08f, -1f, 0.04f),
            new(-0.48f, -0.82f, 0.32f),
        };

        private static readonly Vector3[] VioletGiantMonsterNormals =
        {
            new(0.08f, -1f, 0.04f),
            new(-0.48f, -0.82f, 0.32f),
        };

        [MenuItem("Tools/Friend Slop/Repair Scene Wiring")]
        public static void RepairSceneWiring()
        {
            RepairSceneDefinitionAssets();
            ExtractSceneOwnedWrapper(PrototypeScenePath, ShipInteriorScenePath, ShipInteriorRootName);
            ExtractSceneOwnedWrapper(PrototypeScenePath, VioletGiantScenePath, "Tier 3 Planet");
            RepairPrototypeScene();
            RepairShipInteriorScene();
            RepairPlanetScene(StarterJunkScenePath, "Crash Dirt Patch");
            RepairPlanetScene(RustyMoonScenePath, null);
            RepairPlanetScene(VioletGiantScenePath, null);
            AssetDatabase.SaveAssets();
        }

        public static void RepairSceneWiringBatch()
        {
            RepairSceneWiring();
        }

        private static void RepairPrototypeScene()
        {
            var scene = EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);
            var changed = false;

            changed |= WireSceneCatalog(scene);
            changed |= ClearBootstrapperShipSpawnReferences(scene);

            var ship = FindSceneObject(scene, ShipInteriorRootName);
            if (ship != null && !HasPadWithDestination(ship.transform, TeleporterTarget.ActivePlanet))
            {
                var pad = CreatePadPrimitive("Ship Teleporter Pad", new Color(0.3f, 0.6f, 1f));
                pad.transform.SetParent(ship.transform, false);
                pad.transform.localPosition = new Vector3(4f, 0.05f, 0f);
                pad.transform.localRotation = Quaternion.identity;
                ConfigurePad(pad, TeleporterTarget.ActivePlanet);
                changed = true;
            }

            var envs = Object.FindObjectsByType<PlanetEnvironment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < envs.Length; i++)
            {
                var env = envs[i];
                if (env == null || env.gameObject.scene != scene) continue;
                changed |= RepairPlanetEnvironment(scene, env, "Part Launchpad");
            }

            if (changed)
                EditorSceneManager.SaveScene(scene);
        }

        private static bool ClearBootstrapperShipSpawnReferences(Scene scene)
        {
            var bootstrappers = Object.FindObjectsByType<PrototypeNetworkBootstrapper>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var changed = false;

            for (var i = 0; i < bootstrappers.Length; i++)
            {
                var bootstrapper = bootstrappers[i];
                if (bootstrapper == null || bootstrapper.gameObject.scene != scene) continue;

                var so = new SerializedObject(bootstrapper);
                var prop = so.FindProperty("shipSpawnPoints");
                if (prop == null || prop.arraySize == 0) continue;

                prop.arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bootstrapper);
                changed = true;
            }

            return changed;
        }

        private static void RepairShipInteriorScene()
        {
            var scene = EditorSceneManager.OpenScene(ShipInteriorScenePath, OpenSceneMode.Single);
            var changed = false;

            var ship = FindSceneObject(scene, ShipInteriorRootName);
            if (ship != null)
            {
                if (ship.transform.position != ShipInteriorRootPosition)
                {
                    ship.transform.position = ShipInteriorRootPosition;
                    EditorUtility.SetDirty(ship.transform);
                    changed = true;
                }

                var env = ship.GetComponent<ShipEnvironment>();
                if (env == null)
                {
                    env = ship.AddComponent<ShipEnvironment>();
                    changed = true;
                }

                changed |= WireShipEnvironment(scene, env);

                if (!HasPadWithDestination(ship.transform, TeleporterTarget.ActivePlanet))
                {
                    var pad = CreatePadPrimitive("Ship Teleporter Pad", new Color(0.3f, 0.6f, 1f));
                    pad.transform.SetParent(ship.transform, false);
                    pad.transform.localPosition = new Vector3(4f, 0.05f, 0f);
                    pad.transform.localRotation = Quaternion.identity;
                    ConfigurePad(pad, TeleporterTarget.ActivePlanet);
                    changed = true;
                }
            }

            if (changed)
                EditorSceneManager.SaveScene(scene);
        }

        private static bool WireSceneCatalog(Scene scene)
        {
            var service = Object.FindFirstObjectByType<NetworkSceneTransitionService>(FindObjectsInactive.Include);
            if (service == null || service.gameObject.scene != scene || service.Catalog != null)
                return false;

            var catalog = AssetDatabase.LoadAssetAtPath<GameSceneCatalog>(SceneCatalogPath);
            if (catalog == null) return false;

            var so = new SerializedObject(service);
            var prop = so.FindProperty("sceneCatalog");
            if (prop == null) return false;

            prop.objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(service);
            return true;
        }

        private static void RepairPlanetScene(string scenePath, string preferredLaunchpadName)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var changed = false;

            var envs = Object.FindObjectsByType<PlanetEnvironment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < envs.Length; i++)
            {
                var env = envs[i];
                if (env == null || env.gameObject.scene != scene) continue;
                changed |= RepairPlanetEnvironment(scene, env, preferredLaunchpadName);
            }

            if (changed)
                EditorSceneManager.SaveScene(scene);
        }

        private static bool RepairPlanetEnvironment(Scene scene, PlanetEnvironment env, string preferredLaunchpadName)
        {
            var changed = false;
            var zone = env.LaunchpadZone;
            if (zone == null)
            {
                zone = env.GetComponentInChildren<LaunchpadZone>(true);
                if (zone == null && env.ContentRoot != null)
                    zone = env.ContentRoot.GetComponentInChildren<LaunchpadZone>(true);
                if (zone == null)
                    zone = AddLaunchpadZoneToCandidate(scene, preferredLaunchpadName);
                if (zone != null)
                    changed |= WireLaunchpadZone(env, zone);
            }

            var world = ResolveSphereWorld(env);
            if (world != null && env.SphereWorld == null)
            {
                env.SetSphereWorld(world);
                EditorUtility.SetDirty(env);
                changed = true;
            }

            if (zone != null)
            {
                changed |= EnsureReturnTeleporter(env, zone, world);
            }

            changed |= EnsureLootAnchorsFromSpawner(env);

            if (scene.path == RustyMoonScenePath)
                changed |= EnsureMonsterAnchors(env, world, "Tier 2 Planet Monster Spawn Points", "Tier 2 Planet Monster Spawn", RustyMoonMonsterNormals);
            else if (scene.path == VioletGiantScenePath)
                changed |= EnsureMonsterAnchors(env, world, "Tier 3 Planet Monster Spawn Points", "Tier 3 Planet Monster Spawn", VioletGiantMonsterNormals);

            return changed;
        }

        private static void RepairSceneDefinitionAssets()
        {
            var shipScene = EnsureSceneDefinition(
                ShipInteriorSceneDefinitionPath,
                "Ship Interior",
                ShipInteriorScenePath,
                GameSceneRole.ShipInterior);
            EnsureSceneCatalogContains(shipScene);
            EnsureSceneInBuildSettings(ShipInteriorScenePath);

            var violetScene = EnsureSceneDefinition(
                VioletGiantSceneDefinitionPath,
                "Violet Giant",
                VioletGiantScenePath,
                GameSceneRole.Planet);
            EnsurePlanetSceneAssignment(VioletGiantPlanetPath, violetScene);
            EnsureSceneCatalogContains(violetScene);
            EnsureSceneInBuildSettings(VioletGiantScenePath);
        }

        private static GameSceneDefinition EnsureSceneDefinition(
            string assetPath,
            string displayName,
            string scenePath,
            GameSceneRole role)
        {
            var definition = AssetDatabase.LoadAssetAtPath<GameSceneDefinition>(assetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<GameSceneDefinition>();
                AssetDatabase.CreateAsset(definition, assetPath);
            }

            var so = new SerializedObject(definition);
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("scenePath").stringValue = scenePath;
            so.FindProperty("role").enumValueIndex = (int)role;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void EnsurePlanetSceneAssignment(string planetPath, GameSceneDefinition sceneDefinition)
        {
            if (sceneDefinition == null) return;

            var planet = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(planetPath);
            if (planet == null) return;

            var so = new SerializedObject(planet);
            var prop = so.FindProperty("planetScene");
            if (prop == null || prop.objectReferenceValue == sceneDefinition) return;

            prop.objectReferenceValue = sceneDefinition;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(planet);
        }

        private static void EnsureSceneCatalogContains(GameSceneDefinition sceneDefinition)
        {
            if (sceneDefinition == null) return;

            var catalog = AssetDatabase.LoadAssetAtPath<GameSceneCatalog>(SceneCatalogPath);
            if (catalog == null) return;

            var so = new SerializedObject(catalog);
            var prop = so.FindProperty("scenes");
            if (prop == null) return;

            for (var i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue == sceneDefinition)
                    return;
            }

            prop.InsertArrayElementAtIndex(prop.arraySize);
            prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = sceneDefinition;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void ExtractSceneOwnedWrapper(string prototypeScenePath, string targetScenePath, string wrapperName)
        {
            var protoScene = EditorSceneManager.OpenScene(prototypeScenePath, OpenSceneMode.Single);
            EnsureTargetSceneExists(targetScenePath, out var targetScene);

            var wrapper = FindRoot(targetScene, wrapperName);
            var changed = false;
            if (wrapper == null)
            {
                wrapper = FindRoot(protoScene, wrapperName);
                if (wrapper == null) return;
                EditorSceneManager.MoveGameObjectToScene(wrapper, targetScene);
                changed = true;
            }

            if (!wrapper.activeSelf)
            {
                wrapper.SetActive(true);
                changed = true;
            }

            if (!changed) return;

            EditorSceneManager.MarkSceneDirty(protoScene);
            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(protoScene);
            EditorSceneManager.SaveScene(targetScene);
            EnsureSceneInBuildSettings(targetScenePath);
        }

        private static void EnsureTargetSceneExists(string targetScenePath, out Scene targetScene)
        {
            if (File.Exists(targetScenePath))
            {
                var existing = SceneManager.GetSceneByPath(targetScenePath);
                targetScene = existing.isLoaded
                    ? existing
                    : EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Additive);
                return;
            }

            var dir = Path.GetDirectoryName(targetScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            targetScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(targetScene, targetScenePath);
        }

        private static bool EnsureLootAnchorsFromSpawner(PlanetEnvironment env)
        {
            if (env == null) return false;

            var spawner = env.GetComponentInChildren<PlanetLootSpawner>(true);
            if (spawner == null || !HasAnyLiveTransform(spawner.SpawnPoints)) return false;

            return WireTransformArray(env, "lootSpawnPoints", spawner.SpawnPoints);
        }

        private static bool EnsureMonsterAnchors(
            PlanetEnvironment env,
            SphereWorld world,
            string rootName,
            string childPrefix,
            IReadOnlyList<Vector3> normals)
        {
            if (env == null || world == null || normals == null || normals.Count == 0)
                return false;

            var parent = env.ContentRoot != null ? env.ContentRoot.transform : env.transform;
            var root = FindDirectChild(parent, rootName);
            var changed = false;
            if (root == null)
            {
                var rootGo = new GameObject(rootName);
                rootGo.transform.SetParent(parent, false);
                root = rootGo.transform;
                changed = true;
            }

            var points = new Transform[normals.Count];
            for (var i = 0; i < normals.Count; i++)
            {
                var childName = $"{childPrefix} {i + 1}";
                var child = FindDirectChild(root, childName);
                if (child == null)
                {
                    var go = new GameObject(childName);
                    go.transform.SetParent(root, false);
                    child = go.transform;
                    changed = true;
                }

                var normal = normals[i].normalized;
                child.position = world.GetSurfacePoint(normal, 0.7f);
                child.rotation = world.GetSurfaceRotation(normal, Vector3.forward);
                points[i] = child;
                EditorUtility.SetDirty(child);
            }

            changed |= WireTransformArray(env, "monsterSpawnPoints", points);
            if (changed)
            {
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(env);
            }
            return changed;
        }

        private static bool WireShipEnvironment(Scene scene, ShipEnvironment env)
        {
            if (env == null) return false;

            var spawnRoot = FindSceneObject(scene, "Ship Spawn Points");
            if (spawnRoot == null) return false;

            var spawns = new List<Transform>();
            for (var i = 0; i < spawnRoot.transform.childCount; i++)
            {
                var child = spawnRoot.transform.GetChild(i);
                if (child != null)
                    spawns.Add(child);
            }

            var so = new SerializedObject(env);
            var prop = so.FindProperty("shipSpawnPoints");
            if (prop == null) return false;

            var changed = prop.arraySize != spawns.Count;
            if (prop.arraySize != spawns.Count)
                prop.arraySize = spawns.Count;

            for (var i = 0; i < spawns.Count; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == spawns[i]) continue;
                element.objectReferenceValue = spawns[i];
                changed = true;
            }

            if (!changed) return false;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(env);
            return true;
        }

        private static bool WireTransformArray(PlanetEnvironment env, string serializedField, IReadOnlyList<Transform> transforms)
        {
            if (env == null || transforms == null) return false;

            var so = new SerializedObject(env);
            var prop = so.FindProperty(serializedField);
            if (prop == null) return false;

            var changed = prop.arraySize != transforms.Count;
            if (prop.arraySize != transforms.Count)
                prop.arraySize = transforms.Count;

            for (var i = 0; i < transforms.Count; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == transforms[i]) continue;
                element.objectReferenceValue = transforms[i];
                changed = true;
            }

            if (!changed) return false;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(env);
            return true;
        }

        private static LaunchpadZone AddLaunchpadZoneToCandidate(Scene scene, string preferredLaunchpadName)
        {
            var candidate = !string.IsNullOrEmpty(preferredLaunchpadName)
                ? FindSceneObject(scene, preferredLaunchpadName)
                : null;

            candidate ??= FindSceneObject(scene, "Part Launchpad");
            candidate ??= FindSceneObject(scene, "Launchpad Assembly Site");
            candidate ??= FindSceneObject(scene, "Crash Dirt Patch");
            if (candidate == null) return null;

            var zone = candidate.GetComponent<LaunchpadZone>();
            if (zone == null)
            {
                zone = candidate.AddComponent<LaunchpadZone>();
                EditorUtility.SetDirty(candidate);
            }

            return zone;
        }

        private static bool WireLaunchpadZone(PlanetEnvironment env, LaunchpadZone zone)
        {
            if (env == null || zone == null || env.LaunchpadZone == zone) return false;

            var so = new SerializedObject(env);
            var prop = so.FindProperty("launchpadZone");
            if (prop == null) return false;

            prop.objectReferenceValue = zone;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(env);
            return true;
        }

        private static bool EnsureReturnTeleporter(PlanetEnvironment env, LaunchpadZone zone, SphereWorld world)
        {
            var parent = env.ContentRoot != null ? env.ContentRoot.transform : env.transform;
            if (HasPadWithDestination(parent, TeleporterTarget.Ship))
                return false;

            var pad = CreatePadPrimitive("Ship Return Teleporter Pad", new Color(1f, 0.55f, 0.3f));
            pad.transform.SetParent(parent, true);

            if (world != null)
            {
                var center = world.Center;
                var launchDir = zone.transform.position - center;
                if (launchDir.sqrMagnitude < 0.001f)
                    launchDir = Vector3.up;
                launchDir.Normalize();

                var tangent = Vector3.Cross(launchDir, Vector3.right);
                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.Cross(launchDir, Vector3.forward);
                tangent.Normalize();

                var dir = (Quaternion.AngleAxis(18f, tangent) * launchDir).normalized;
                pad.transform.position = world.GetSurfacePoint(dir, 0.12f);
                pad.transform.rotation = world.GetSurfaceRotation(dir, Vector3.forward);
            }
            else
            {
                pad.transform.localPosition = zone.transform.localPosition + new Vector3(4f, 0.12f, 0f);
                pad.transform.localRotation = zone.transform.localRotation;
            }

            ConfigurePad(pad, TeleporterTarget.Ship);
            EditorUtility.SetDirty(pad);
            return true;
        }

        private static SphereWorld ResolveSphereWorld(PlanetEnvironment env)
        {
            if (env == null) return null;
            if (env.SphereWorld != null) return env.SphereWorld;

            var world = env.GetComponentInChildren<SphereWorld>(true);
            if (world != null) return world;

            return env.ContentRoot != null ? env.ContentRoot.GetComponentInChildren<SphereWorld>(true) : null;
        }

        private static GameObject CreatePadPrimitive(string name, Color color)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = name;
            pad.transform.localScale = new Vector3(2f, 0.06f, 2f);

            var capsule = pad.GetComponent<Collider>();
            if (capsule != null) capsule.isTrigger = false;

            var trigger = pad.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.6f, 12f, 1.6f);
            trigger.center = new Vector3(0f, 6f, 0f);

            ApplyEmissiveMaterial(pad, color);
            return pad;
        }

        private static void ConfigurePad(GameObject pad, TeleporterTarget destination)
        {
            var teleporter = pad.GetComponent<TeleporterPad>();
            if (teleporter == null)
                teleporter = pad.AddComponent<TeleporterPad>();

            var so = new SerializedObject(teleporter);
            var prop = so.FindProperty("destination");
            if (prop != null)
                prop.enumValueIndex = (int)destination;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(teleporter);
        }

        private static void ApplyEmissiveMaterial(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color, name = $"{go.name} Material" };
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.6f);
            }
            renderer.sharedMaterial = mat;
        }

        private static bool HasPadWithDestination(Transform parent, TeleporterTarget destination)
        {
            if (parent == null) return false;
            var pads = parent.GetComponentsInChildren<TeleporterPad>(true);
            for (var i = 0; i < pads.Length; i++)
                if (pads[i] != null && pads[i].Destination == destination) return true;
            return false;
        }

        private static bool HasAnyLiveTransform(IReadOnlyList<Transform> transforms)
        {
            if (transforms == null) return false;
            for (var i = 0; i < transforms.Count; i++)
            {
                if (transforms[i] != null)
                    return true;
            }
            return false;
        }

        private static Transform FindDirectChild(Transform parent, string objectName)
        {
            if (parent == null || string.IsNullOrEmpty(objectName)) return null;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == objectName)
                    return child;
            }
            return null;
        }

        private static GameObject FindRoot(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(objectName)) return null;

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == objectName)
                    return roots[i];
            }
            return null;
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            scenePath = scenePath.Replace('\\', '/');
            var current = EditorBuildSettings.scenes;
            for (var i = 0; i < current.Length; i++)
            {
                var existing = current[i].path != null
                    ? current[i].path.Replace('\\', '/')
                    : string.Empty;
                if (string.Equals(existing, scenePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    var changed = false;
                    if (!current[i].enabled)
                    {
                        current[i].enabled = true;
                        changed = true;
                    }

                    var assetGuid = AssetDatabase.AssetPathToGUID(scenePath);
                    if (!string.IsNullOrEmpty(assetGuid)
                        && !string.Equals(current[i].guid.ToString(), assetGuid, System.StringComparison.OrdinalIgnoreCase))
                    {
                        current[i] = new EditorBuildSettingsScene(scenePath, true);
                        changed = true;
                    }

                    if (changed)
                        EditorBuildSettings.scenes = current;
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[current.Length + 1];
            System.Array.Copy(current, updated, current.Length);
            updated[current.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;

            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || t.gameObject.scene != scene) continue;
                if (t.name == objectName) return t.gameObject;
            }
            return null;
        }
    }
}
#endif
