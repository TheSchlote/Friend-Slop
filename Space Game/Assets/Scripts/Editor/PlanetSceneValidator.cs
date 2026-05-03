using System.Collections.Generic;
using System.IO;
using FriendSlop.Core;
using FriendSlop.Loot;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using FriendSlop.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static class PlanetSceneValidator
    {
        private const string BootstrapScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
        private const string ShipInteriorRootName = "Bigger-On-The-Inside Ship Interior";

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
            ValidateSceneCatalogShipInterior(failures);
            ValidateCatalogPlanetScenesRoundReady(failures);
            ValidateBootstrapDoesNotOwnShipInterior(failures);
            ValidateNoSceneOwnedPlanetsNestedInBootstrap(failures);
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

                        if (!HasAnyLiveTransform(environment.MonsterSpawnPoints))
                            failures.Add($"{label}: PlanetEnvironment '{environment.name}' has no live monster spawn anchors.");

                        ValidateSceneLootOwnership(scene, environment, label, failures);

                        if (!HasTeleporterToShip(scene))
                            failures.Add($"{label}: scene has no TeleporterPad targeting Ship.");
                    });
                }
            }
        }

        public static void ValidateBootstrapShipTeleporter(List<string> failures)
        {
            ValidateSceneCatalogShipInterior(failures);
        }

        public static void ValidateSceneCatalogShipInterior(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            var buildScenes = LoadBuildSettingsScenePaths();
            var sawCatalog = false;

            foreach (var catalog in LoadAllAssets<GameSceneCatalog>())
            {
                sawCatalog = true;
                var scene = catalog.GetFirstByRole(GameSceneRole.ShipInterior);
                if (scene == null)
                {
                    failures.Add($"{catalog.name}: no ShipInterior scene definition is registered.");
                    continue;
                }

                if (!scene.IsConfigured)
                {
                    failures.Add($"{catalog.name} -> {scene.name}: ShipInterior scene path is not configured.");
                    continue;
                }

                ValidateScenePathInBuildSettings(buildScenes, scene.ScenePath, $"{catalog.name} -> {scene.name}", failures);
                if (!File.Exists(scene.ScenePath))
                    continue;

                WithScene(scene.ScenePath, loadedScene =>
                {
                    var environments = GetComponentsInScene<ShipEnvironment>(loadedScene);
                    if (environments.Length == 0)
                    {
                        failures.Add($"{catalog.name} -> {scene.name}: no ShipEnvironment was found.");
                    }
                    else if (!HasAnyLiveTransform(environments[0].ShipSpawnPoints))
                    {
                        failures.Add($"{catalog.name} -> {scene.name}: ShipEnvironment has no live ship spawn points.");
                    }

                    if (!HasTeleporterToActivePlanet(loadedScene))
                        failures.Add($"{catalog.name} -> {scene.name}: scene has no TeleporterPad targeting ActivePlanet.");

                    if (GetComponentsInScene<ShipStation>(loadedScene).Length == 0)
                        failures.Add($"{catalog.name} -> {scene.name}: scene has no ShipStation components.");
                });
            }

            if (!sawCatalog)
                failures.Add("No GameSceneCatalog assets were found.");
        }

        public static void ValidateBootstrapDoesNotOwnShipInterior(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            if (!File.Exists(BootstrapScenePath))
            {
                failures.Add($"Bootstrap scene does not exist at '{BootstrapScenePath}'.");
                return;
            }

            WithScene(BootstrapScenePath, scene =>
            {
                if (FindRoot(scene, ShipInteriorRootName) != null)
                    failures.Add($"Bootstrap scene still owns ship root '{ShipInteriorRootName}'.");

                var environments = GetComponentsInScene<ShipEnvironment>(scene);
                if (environments.Length > 0)
                    failures.Add("Bootstrap scene should not contain a ShipEnvironment; ship content belongs in ShipInterior.");

                var stations = GetComponentsInScene<ShipStation>(scene);
                if (stations.Length > 0)
                    failures.Add("Bootstrap scene should not contain ShipStation components; ship content belongs in ShipInterior.");
            });
        }

        public static void ValidateNoSceneOwnedPlanetsNestedInBootstrap(List<string> failures)
        {
            if (failures == null) throw new System.ArgumentNullException(nameof(failures));

            if (!File.Exists(BootstrapScenePath))
                return;

            WithScene(BootstrapScenePath, scene =>
            {
                var environments = GetComponentsInScene<PlanetEnvironment>(scene);
                for (var i = 0; i < environments.Length; i++)
                {
                    var env = environments[i];
                    if (env == null || env.Planet == null || !env.Planet.HasPlanetScene) continue;
                    failures.Add(
                        $"Bootstrap scene still contains scene-owned PlanetEnvironment '{env.name}' for '{env.Planet.name}'.");
                }
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

        private static void ValidateSceneLootOwnership(
            Scene scene,
            PlanetEnvironment environment,
            string label,
            List<string> failures)
        {
            var spawner = FindPlanetLootSpawner(scene, environment);
            if (spawner == null) return;

            if (!HasAnyLiveTransform(spawner.SpawnPoints))
                failures.Add($"{label}: PlanetLootSpawner '{spawner.name}' has no live spawn points.");

            if (!HasAnyLiveTransform(environment.LootSpawnPoints))
            {
                failures.Add(
                    $"{label}: PlanetEnvironment '{environment.name}' does not expose the scene loot anchors owned by PlanetLootSpawner '{spawner.name}'.");
            }
        }

        private static PlanetLootSpawner FindPlanetLootSpawner(Scene scene, PlanetEnvironment environment)
        {
            if (environment != null)
            {
                var owned = environment.GetComponentInChildren<PlanetLootSpawner>(true);
                if (owned != null) return owned;
            }

            var spawners = GetComponentsInScene<PlanetLootSpawner>(scene);
            return spawners.Length > 0 ? spawners[0] : null;
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
