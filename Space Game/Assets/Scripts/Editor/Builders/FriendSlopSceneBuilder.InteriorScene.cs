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

            // Spawn point one unit above origin facing +Z
            var spawnGo = new GameObject("SpawnPoint");
            spawnGo.transform.SetParent(go.transform, false);
            spawnGo.transform.localPosition = new Vector3(0f, 1f, -1f);

            var doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{InteriorPrefabFolder}/InteriorDoor.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<InteriorCatalog>(
                $"{InteriorAssetFolder}/InteriorCatalog.asset");

            var so = new SerializedObject(bs);
            so.FindProperty("spawnPoint").objectReferenceValue = spawnGo.transform;
            so.FindProperty("doorPrefab").objectReferenceValue = doorPrefab;
            so.FindProperty("catalog").objectReferenceValue    = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
            return true;
        }

        // ── Exit door ──────────────────────────────────────────────────────────

        private static bool EnsureExitDoor(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<InteriorExitDoor>() != null) return false;

            var origin = InteriorSessionData.InteriorWorldOrigin;

            var go = new GameObject("InteriorExitDoor");
            SceneManager.MoveGameObjectToScene(go, scene);

            // Place 2 m in front of the spawn point so the player sees it immediately.
            go.transform.position = origin + new Vector3(0f, 1f, 0f);

            go.AddComponent<NetworkObject>();
            go.AddComponent<InteriorExitDoor>();

            // Non-trigger BoxCollider so SphereCast in PlayerInteractor can detect it.
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;
            col.size = new Vector3(1.5f, 2.5f, 0.2f);

            // Visible placeholder geometry (thin slab facing +Z)
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "ExitDoor_Visual";
            slab.transform.SetParent(go.transform, false);
            slab.transform.localScale = new Vector3(1.5f, 2.5f, 0.1f);
            Object.DestroyImmediate(slab.GetComponent<Collider>());

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
