#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    // Same shape as the Tier 1 extractor, but the Tier 2 wrapper authored by
    // FriendSlopMultiPlanetBuilder already has all its content as direct children, so
    // there's nothing to reparent first - one MoveGameObjectToScene takes everything.
    public static class FriendSlopExtractRustyMoonScene
    {
        private const string PrototypeScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
        private const string TargetScenePath = "Assets/Scenes/Planet_RustyMoon.unity";
        private const string PlanetWrapperName = "Tier 2 Planet";

        [MenuItem("Tools/Friend Slop/Extract Rusty Moon Into Scene")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);

            EnsureTargetSceneExists(out var targetScene);
            var protoScene = SceneManager.GetSceneByPath(PrototypeScenePath);

            // Idempotency: if the wrapper is already in the target scene, just refresh
            // the active flag + Build Settings entry and exit cleanly.
            var wrapperInTarget = FindRoot(targetScene, PlanetWrapperName);
            if (wrapperInTarget != null)
            {
                if (!wrapperInTarget.activeSelf) wrapperInTarget.SetActive(true);
                EditorSceneManager.MarkSceneDirty(targetScene);
                EditorSceneManager.SaveScene(targetScene);
                EnsureSceneInBuildSettings(TargetScenePath);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Friend Slop",
                    $"'{PlanetWrapperName}' is already in {TargetScenePath}. " +
                    "Refreshed active state and Build Settings entry.",
                    "OK");
                return;
            }

            var wrapper = FindRoot(protoScene, PlanetWrapperName);
            if (wrapper == null)
            {
                Debug.LogWarning(
                    $"Friend Slop: '{PlanetWrapperName}' wrapper not found in {PrototypeScenePath} or {TargetScenePath}; " +
                    "run Tools/Friend Slop/Build Tier 2 + Tier 3 Planets first.");
                return;
            }

            if (wrapper.scene != targetScene)
                EditorSceneManager.MoveGameObjectToScene(wrapper, targetScene);

            // The multi-planet builder leaves Tier 2+ inactive in the prototype scene so
            // the toggle path can enable it on demand. Once it lives in its own scene, the
            // scene-load IS the activation signal - the wrapper needs to be active when the
            // scene additively loads so its PlanetEnvironment Awakens and registers.
            if (!wrapper.activeSelf) wrapper.SetActive(true);

            EditorSceneManager.MarkSceneDirty(protoScene);
            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(protoScene);
            EditorSceneManager.SaveScene(targetScene);

            EnsureSceneInBuildSettings(TargetScenePath);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Friend Slop",
                $"Extracted Tier 2 (Rusty Moon) into {TargetScenePath}.\n\n" +
                "Both scenes saved. Planet_RustyMoon.unity is now in Build Settings.",
                "OK");
        }

        private static void EnsureTargetSceneExists(out Scene targetScene)
        {
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

            targetScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(targetScene, TargetScenePath);
        }

        private static GameObject FindRoot(Scene scene, string objectName)
        {
            var rootObjects = scene.GetRootGameObjects();
            for (var i = 0; i < rootObjects.Length; i++)
            {
                if (rootObjects[i] != null && rootObjects[i].name == objectName)
                    return rootObjects[i];
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
