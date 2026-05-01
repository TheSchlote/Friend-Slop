#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FriendSlop.Round;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    // Tools menu entry that splits Tier 1 (Starter Junk) out of the prototype scene
    // and into its own additively-loaded scene file. Run once per project; idempotent
    // afterward.
    public static class FriendSlopExtractStarterJunkScene
    {
        private const string PrototypeScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
        private const string TargetScenePath = "Assets/Scenes/Planet_StarterJunk.unity";
        private const string PlanetWrapperName = "Tier 1 Planet";

        // Tier 1 GameObjects that historically lived as separate top-level roots in the
        // prototype scene. We reparent them under the wrapper so MoveGameObjectToScene
        // pulls everything across in one shot.
        private static readonly string[] Tier1SatelliteRootNames =
        {
            "Tiny Sphere World",
            "Tiny Planet Sun",
            "Part Launchpad",
            "Player Spawn 1",
            "Player Spawn 2",
            "Player Spawn 3",
            "Player Spawn 4",
            "Loot Spawn Points",
            "Enemy Spawn Points",
        };

        [MenuItem("Tools/Friend Slop/Extract Tier 1 Into Scene")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var protoScene = EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);

            EnsureTargetSceneExists(out var targetScene);
            // Re-fetch the prototype scene handle: opening additively above does not change
            // it, but the bookkeeping is clearer.
            protoScene = SceneManager.GetSceneByPath(PrototypeScenePath);

            // Idempotency: if the wrapper is already in the target scene from a previous
            // extraction, just re-run the loot/build-settings wiring and exit. Otherwise
            // pull it out of the prototype scene like the first run does.
            var wrapperInTarget = FindRootByName(targetScene, PlanetWrapperName);
            if (wrapperInTarget != null)
            {
                WireSpawnPointArray(wrapperInTarget, "Loot Spawn Points", "lootSpawnPoints");
                WireSpawnPointArray(wrapperInTarget, "Enemy Spawn Points", "monsterSpawnPoints");
                EditorSceneManager.MarkSceneDirty(targetScene);
                EditorSceneManager.SaveScene(targetScene);
                EnsureSceneInBuildSettings(TargetScenePath);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Friend Slop",
                    $"'{PlanetWrapperName}' is already in {TargetScenePath}. " +
                    "Refreshed loot/monster anchor wiring and Build Settings entry.",
                    "OK");
                return;
            }

            var wrapper = ReparentTier1Satellites(protoScene);
            if (wrapper == null)
            {
                Debug.LogWarning(
                    $"Friend Slop: '{PlanetWrapperName}' wrapper not found in {PrototypeScenePath} or {TargetScenePath}; nothing to move.");
                return;
            }

            // If the wrapper is already in the target scene from a previous extraction, this
            // call is a no-op.
            if (wrapper.scene != targetScene)
                EditorSceneManager.MoveGameObjectToScene(wrapper, targetScene);

            WireSpawnPointArray(wrapper, "Loot Spawn Points", "lootSpawnPoints");
            WireSpawnPointArray(wrapper, "Enemy Spawn Points", "monsterSpawnPoints");

            EditorSceneManager.MarkSceneDirty(protoScene);
            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(protoScene);
            EditorSceneManager.SaveScene(targetScene);

            EnsureSceneInBuildSettings(TargetScenePath);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Friend Slop",
                $"Extracted Tier 1 wrapper into {TargetScenePath}.\n\n" +
                "Both scenes saved. Planet_StarterJunk.unity is now in Build Settings.",
                "OK");
        }

        private static void EnsureTargetSceneExists(out Scene targetScene)
        {
            // If the .unity file already exists, just open it additively.
            if (File.Exists(TargetScenePath))
            {
                var existing = SceneManager.GetSceneByPath(TargetScenePath);
                targetScene = existing.isLoaded
                    ? existing
                    : EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive);
                return;
            }

            var dir = Path.GetDirectoryName(TargetScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Empty scene = no camera/light, since the planet brings its own lighting and
            // there's no player camera in this scene.
            targetScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(targetScene, TargetScenePath);
        }

        private static GameObject FindRootByName(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var rootObjects = scene.GetRootGameObjects();
            for (var i = 0; i < rootObjects.Length; i++)
            {
                if (rootObjects[i] != null && rootObjects[i].name == objectName)
                    return rootObjects[i];
            }
            return null;
        }

        private static GameObject ReparentTier1Satellites(Scene protoScene)
        {
            var wrapper = FindRootByName(protoScene, PlanetWrapperName);
            if (wrapper == null) return null;
            var rootObjects = protoScene.GetRootGameObjects();

            var satelliteSet = new HashSet<string>(Tier1SatelliteRootNames);
            foreach (var root in rootObjects)
            {
                if (root == null || root == wrapper) continue;
                if (!satelliteSet.Contains(root.name)) continue;

                // Preserve world-space placement: spawns and the launchpad were authored on
                // the planet surface and must not jump when reparented.
                Undo.SetTransformParent(root.transform, wrapper.transform, "Reparent Tier 1 satellite");
            }

            return wrapper;
        }

        private static void WireSpawnPointArray(GameObject wrapper, string anchorParentName, string serializedField)
        {
            var env = wrapper.GetComponent<PlanetEnvironment>();
            if (env == null) return;

            Transform anchorRoot = null;
            for (var i = 0; i < wrapper.transform.childCount; i++)
            {
                var child = wrapper.transform.GetChild(i);
                if (child != null && child.name == anchorParentName)
                {
                    anchorRoot = child;
                    break;
                }
            }

            if (anchorRoot == null) return;

            var anchors = new Transform[anchorRoot.childCount];
            for (var i = 0; i < anchorRoot.childCount; i++)
                anchors[i] = anchorRoot.GetChild(i);

            var so = new SerializedObject(env);
            var prop = so.FindProperty(serializedField);
            if (prop == null) return;
            prop.arraySize = anchors.Length;
            for (var i = 0; i < anchors.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(env);
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
                    if (!current[i].enabled)
                    {
                        current[i].enabled = true;
                        EditorBuildSettings.scenes = current;
                    }
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[current.Length + 1];
            System.Array.Copy(current, updated, current.Length);
            updated[current.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
#endif
