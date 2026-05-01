#if UNITY_EDITOR
using FriendSlop.Core;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static class FriendSlopSceneWiringRepair
    {
        private const string PrototypeScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
        private const string StarterJunkScenePath = "Assets/Scenes/Planet_StarterJunk.unity";
        private const string RustyMoonScenePath = "Assets/Scenes/Planet_RustyMoon.unity";
        private const string SceneCatalogPath = "Assets/SceneDefinitions/MainGameSceneCatalog.asset";

        [MenuItem("Tools/Friend Slop/Repair Scene Wiring")]
        public static void RepairSceneWiring()
        {
            RepairPrototypeScene();
            RepairPlanetScene(StarterJunkScenePath, "Crash Dirt Patch");
            RepairPlanetScene(RustyMoonScenePath, null);
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

            var ship = FindSceneObject(scene, "Bigger-On-The-Inside Ship Interior");
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

            return changed;
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
