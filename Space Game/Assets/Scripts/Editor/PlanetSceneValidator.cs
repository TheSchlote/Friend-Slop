using System.Collections.Generic;
using System.IO;
using FriendSlop.Core;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static class PlanetSceneValidator
    {
        private const string BootstrapScenePath = "Assets/Scenes/FriendSlopPrototype.unity";

        [MenuItem("Tools/Friend Slop/Validate Planet Scenes")]
        public static void ValidateFromMenu()
        {
            if (TryValidate(out var failures))
            {
                Debug.Log("Friend Slop planet scene validation passed.");
                return;
            }

            Debug.LogError(FormatFailureMessage(failures));
        }

        public static void Validate()
        {
            if (TryValidate(out var failures))
            {
                Debug.Log("Friend Slop planet scene validation passed.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
                return;
            }

            var message = FormatFailureMessage(failures);
            Debug.LogError(message);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw new System.InvalidOperationException(message);
        }

        public static bool TryValidate(out List<string> failures)
        {
            failures = new List<string>();
            ValidateCatalogPlanetScenesInBuildSettings(failures);
            ValidateSceneCatalogPlanetEntriesInBuildSettings(failures);
            ValidateCatalogPlanetScenesRoundReady(failures);
            ValidateBootstrapShipTeleporter(failures);
            return failures.Count == 0;
        }

        public static void ValidateCatalogPlanetScenesInBuildSettings(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            var buildScenes = LoadBuildSettingsScenePaths();

            foreach (var catalog in LoadAllAssets<PlanetCatalog>())
            {
                if (catalog == null || catalog.AllPlanets == null) continue;

                for (var i = 0; i < catalog.AllPlanets.Count; i++)
                {
                    var planet = catalog.AllPlanets[i];
                    if (planet == null || !planet.HasPlanetScene) continue;

                    var scenePath = planet.PlanetScene.ScenePath;
                    var label = $"{catalog.name} -> {planet.name} -> {planet.PlanetScene.name}";
                    ValidateScenePathInBuildSettings(buildScenes, scenePath, label, failures);
                }
            }
        }

        public static void ValidateSceneCatalogPlanetEntriesInBuildSettings(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            var buildScenes = LoadBuildSettingsScenePaths();

            foreach (var catalog in LoadAllAssets<GameSceneCatalog>())
            {
                if (catalog == null || catalog.AllScenes == null) continue;

                for (var i = 0; i < catalog.AllScenes.Count; i++)
                {
                    var def = catalog.AllScenes[i];
                    if (def == null || def.Role != GameSceneRole.Planet) continue;
                    if (!def.IsConfigured) continue;

                    ValidateScenePathInBuildSettings(buildScenes, def.ScenePath, $"{catalog.name} -> {def.name}", failures);
                }
            }
        }

        public static void ValidateCatalogPlanetScenesRoundReady(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            foreach (var catalog in LoadAllAssets<PlanetCatalog>())
            {
                if (catalog == null || catalog.AllPlanets == null) continue;

                for (var i = 0; i < catalog.AllPlanets.Count; i++)
                {
                    var planet = catalog.AllPlanets[i];
                    if (planet == null || !planet.HasPlanetScene) continue;

                    var scenePath = GameScenePathUtility.NormalizePath(planet.PlanetScene.ScenePath);
                    var label = $"{catalog.name} -> {planet.name} -> {planet.PlanetScene.name}";
                    if (!File.Exists(scenePath))
                    {
                        failures.Add($"{label}: scene file does not exist at '{scenePath}'.");
                        continue;
                    }

                    WithScene(scenePath, scene =>
                    {
                        var environments = GetComponentsInScene<PlanetEnvironment>(scene);
                        var environment = FindCompatibleEnvironment(environments, planet);

                        if (environment == null)
                        {
                            failures.Add($"{label}: no compatible PlanetEnvironment was found.");
                            return;
                        }

                        if (environment.LaunchpadZone == null)
                            failures.Add($"{label}: PlanetEnvironment '{environment.name}' has no LaunchpadZone assigned.");

                        if (!HasAnyLiveTransform(environment.PlayerSpawnPoints))
                            failures.Add($"{label}: PlanetEnvironment '{environment.name}' has no live player spawn points.");

                        if (ResolveSphereWorld(environment) == null)
                            failures.Add($"{label}: PlanetEnvironment '{environment.name}' has no SphereWorld assigned or discoverable.");

                        if (!HasTeleporterToShip(scene))
                            failures.Add($"{label}: scene has no TeleporterPad targeting Ship.");
                    });
                }
            }
        }

        public static void ValidateBootstrapShipTeleporter(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            if (!File.Exists(BootstrapScenePath))
            {
                failures.Add($"Bootstrap scene does not exist at '{BootstrapScenePath}'.");
                return;
            }

            WithScene(BootstrapScenePath, scene =>
            {
                if (!HasTeleporterToActivePlanet(scene))
                    failures.Add("The bootstrap ship scene should include a TeleporterPad targeting ActivePlanet.");
            });
        }

        private static void ValidateScenePathInBuildSettings(
            IReadOnlyDictionary<string, bool> buildScenes,
            string scenePath,
            string label,
            List<string> failures)
        {
            scenePath = GameScenePathUtility.NormalizePath(scenePath);
            if (!File.Exists(scenePath))
            {
                failures.Add($"{label}: scene file does not exist at '{scenePath}'.");
                return;
            }

            if (!buildScenes.TryGetValue(scenePath, out var enabled))
            {
                failures.Add($"{label}: scene '{scenePath}' is not in EditorBuildSettings.");
                return;
            }

            if (!enabled)
                failures.Add($"{label}: scene '{scenePath}' is in EditorBuildSettings but disabled.");
        }

        private static Dictionary<string, bool> LoadBuildSettingsScenePaths()
        {
            var result = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
            var scenes = EditorBuildSettings.scenes;
            for (var i = 0; i < scenes.Length; i++)
            {
                var entry = scenes[i];
                if (entry == null || string.IsNullOrEmpty(entry.path)) continue;
                var normalized = GameScenePathUtility.NormalizePath(entry.path);
                result[normalized] = entry.enabled;
            }
            return result;
        }

        private static IEnumerable<T> LoadAllAssets<T>() where T : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) yield return asset;
            }
        }

        private static void WithScene(string scenePath, System.Action<Scene> inspect)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                inspect(scene);
            }
            finally
            {
                if (!TryRestoreSceneSetup(setup))
                {
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        private static bool TryRestoreSceneSetup(SceneSetup[] setup)
        {
            if (setup == null || setup.Length == 0)
                return false;

            try
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                return true;
            }
            catch (System.ArgumentException)
            {
                return false;
            }
        }

        private static T[] GetComponentsInScene<T>(Scene scene) where T : Component
        {
            var results = new List<T>();
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                results.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }
            return results.ToArray();
        }

        private static PlanetEnvironment FindCompatibleEnvironment(PlanetEnvironment[] environments, PlanetDefinition planet)
        {
            if (environments == null || planet == null) return null;

            for (var i = 0; i < environments.Length; i++)
            {
                var env = environments[i];
                if (env != null && env.Planet == planet)
                    return env;
            }

            for (var i = 0; i < environments.Length; i++)
            {
                var env = environments[i];
                if (env != null && env.Planet != null && env.Planet.Tier == planet.Tier)
                    return env;
            }

            return null;
        }

        private static bool HasAnyLiveTransform(Transform[] transforms)
        {
            if (transforms == null) return false;
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null)
                    return true;
            }
            return false;
        }

        private static SphereWorld ResolveSphereWorld(PlanetEnvironment environment)
        {
            if (environment == null) return null;
            if (environment.SphereWorld != null) return environment.SphereWorld;

            var world = environment.GetComponentInChildren<SphereWorld>(true);
            if (world != null) return world;

            var contentRoot = environment.ContentRoot;
            return contentRoot != null ? contentRoot.GetComponentInChildren<SphereWorld>(true) : null;
        }

        private static bool HasTeleporterToShip(Scene scene)
        {
            var pads = GetComponentsInScene<TeleporterPad>(scene);
            for (var i = 0; i < pads.Length; i++)
            {
                if (pads[i] != null && pads[i].Destination == TeleporterTarget.Ship)
                    return true;
            }
            return false;
        }

        private static bool HasTeleporterToActivePlanet(Scene scene)
        {
            var pads = GetComponentsInScene<TeleporterPad>(scene);
            for (var i = 0; i < pads.Length; i++)
            {
                if (pads[i] != null && pads[i].Destination == TeleporterTarget.ActivePlanet)
                    return true;
            }
            return false;
        }

        private static string FormatFailureMessage(List<string> failures)
        {
            return "Friend Slop planet scene validation failed:\n  - " + string.Join("\n  - ", failures);
        }
    }
}
