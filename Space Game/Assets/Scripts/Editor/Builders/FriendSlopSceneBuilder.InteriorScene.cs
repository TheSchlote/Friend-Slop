#if UNITY_EDITOR
using FriendSlop.Core;
using FriendSlop.Interiors;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private const string InteriorScenePath = "Assets/Scenes/Building_Interior.unity";
        private const string InteriorSceneName = "Building_Interior";

        [MenuItem("Tools/Friend Slop/Interiors/Repair Interior Scene")]
        public static void RepairInteriorScene()
        {
            var scene = LoadOrCreateInteriorScene();
            if (!scene.IsValid()) return;

            var changed = false;
            changed |= EnsureBootstrapper(scene);
            changed |= EnsureExitDoor(scene);
            changed |= EnsureFlatGravityVolume(scene);
            changed |= EnsureSceneInBuildSettings();

            if (changed)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Friend Slop] Interior scene repaired.");
            }
            else
            {
                Debug.Log("[Friend Slop] Interior scene already up to date.");
            }

            // Close the scene so it doesn't auto-load with the editor on Play. If the
            // scene is open when Play starts, the bootstrapper's OnNetworkSpawn fires
            // before any player enters the building, generates with empty session data,
            // and produces an empty interior.
            if (SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(scene, removeScene: true);
        }

        // ── Scene load / create ────────────────────────────────────────────────

        private static Scene LoadOrCreateInteriorScene()
        {
            EnsureFolder("Assets/Scenes");

            var existing = AssetDatabase.AssetPathToGUID(InteriorScenePath);
            if (!string.IsNullOrEmpty(existing))
                return EditorSceneManager.OpenScene(InteriorScenePath, OpenSceneMode.Additive);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, InteriorScenePath);
            Debug.Log($"[Friend Slop] Created {InteriorScenePath}");
            return scene;
        }

        // ── Bootstrapper ───────────────────────────────────────────────────────

        private static bool EnsureBootstrapper(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<InteriorSceneBootstrapper>() != null) return false;

            // Placed at the interior world origin so all generated rooms centre on it.
            var origin = InteriorSessionData.InteriorWorldOrigin;

            var go = new GameObject("InteriorBootstrapper");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = origin;
            go.AddComponent<NetworkObject>();
            var bs = go.AddComponent<InteriorSceneBootstrapper>();

            var doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{InteriorPrefabFolder}/InteriorDoor.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<InteriorCatalog>(
                $"{InteriorAssetFolder}/InteriorCatalog.asset");

            // Leave spawnPoint unwired — bootstrapper computes the entry-room centre at runtime.
            var so = new SerializedObject(bs);
            so.FindProperty("doorPrefab").objectReferenceValue = doorPrefab;
            so.FindProperty("catalog").objectReferenceValue    = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
            return true;
        }

        // ── Exit door ──────────────────────────────────────────────────────────

        private static bool EnsureExitDoor(Scene scene)
        {
            // Destroy both real InteriorExitDoors and orphaned GameObjects named
            // "InteriorExitDoor" (those exist because earlier broken builds tried to add
            // the component before its required Collider and silently dropped it).
            var staleRoots = new System.Collections.Generic.HashSet<GameObject>();
            var allExits = Object.FindObjectsByType<InteriorExitDoor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var exit in allExits)
            {
                if (exit == null || exit.gameObject == null) continue;
                if (exit.gameObject.scene != scene) continue;
                staleRoots.Add(exit.gameObject.transform.root.gameObject);
            }
            foreach (var root in scene.GetRootGameObjects())
                if (root != null && root.name == "InteriorExitDoor")
                    staleRoots.Add(root);

            foreach (var stale in staleRoots)
                Object.DestroyImmediate(stale);
            Debug.Log($"[Friend Slop] EnsureExitDoor: destroyed {staleRoots.Count} stale root(s) in '{scene.name}'.");

            var origin = InteriorSessionData.InteriorWorldOrigin;

            var go = new GameObject("InteriorExitDoor");
            SceneManager.MoveGameObjectToScene(go, scene);
            // Bootstrapper repositions this at runtime to the entry-room centre once the
            // building definition is known. The placement here is just a sane default.
            go.transform.position = origin;

            // Sized to fully fill the room's 2 m × 3 m door-frame opening.
            // Collider must be added BEFORE InteriorExitDoor — [RequireComponent] silently
            // refuses to attach the component otherwise.
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;
            col.size   = new Vector3(2f, 3f, 0.2f);
            col.center = new Vector3(0f, 1.5f, 0f);

            go.AddComponent<NetworkObject>();
            go.AddComponent<InteriorExitDoor>();

            // Slab visual — fills the doorway opening exactly so no gap is visible around it.
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "ExitDoor_Visual";
            slab.transform.SetParent(go.transform, false);
            slab.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            slab.transform.localScale    = new Vector3(2f, 3f, 0.1f);
            Object.DestroyImmediate(slab.GetComponent<Collider>());

            var renderer = slab.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                var red = new Color(0.85f, 0.15f, 0.15f);
                var mat = new Material(renderer.sharedMaterial) { color = red };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", red);
                renderer.sharedMaterial = mat;
            }

            EditorUtility.SetDirty(go);
            return true;
        }

        // ── Flat gravity volume ────────────────────────────────────────────────

        private static bool EnsureFlatGravityVolume(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<FlatGravityVolume>() != null) return false;

            var origin = InteriorSessionData.InteriorWorldOrigin;

            var go = new GameObject("InteriorGravityVolume");
            SceneManager.MoveGameObjectToScene(go, scene);
            // Center the volume 10 m above origin so it spans origin-5 to origin+15.
            go.transform.position = origin + new Vector3(0f, 10f, 0f);

            var fgv = go.AddComponent<FlatGravityVolume>();
            // FlatGravityVolume.Up returns transform.up; default rotation gives Vector3.up — correct for Y=2000.
            // Override size so it covers the entire generated layout (400 × 20 × 400 m).
            var so = new SerializedObject(fgv);
            so.FindProperty("size").vector3Value = new Vector3(400f, 20f, 400f);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
            return true;
        }

        // ── Build settings ─────────────────────────────────────────────────────

        private static bool EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == InteriorScenePath) return false;

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(InteriorScenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[Friend Slop] Added {InteriorSceneName} to build settings.");
            return true;
        }

        // ── Folder helper ──────────────────────────────────────────────────────

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                var folder = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
#endif
