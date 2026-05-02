using System.Collections.Generic;
using System.IO;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Tests.EditMode
{
    // Catches the "I added a planet scene but forgot to drag it into Build Settings"
    // failure mode at test time. Without this, the runtime additive load can fail and
    // strand the host in a loading or lobby phase waiting for an environment that will
    // never load.
    public class PlanetCatalogBuildSettingsTests
    {
        [Test]
        public void EveryCatalogPlanetWithScene_PointsAtBuildSettingsEntry()
        {
            var failures = new List<string>();
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

                    if (!File.Exists(scenePath))
                    {
                        failures.Add($"{label}: scene file does not exist at '{scenePath}'.");
                        continue;
                    }

                    if (!buildScenes.TryGetValue(scenePath, out var enabled))
                    {
                        failures.Add($"{label}: scene '{scenePath}' is not in EditorBuildSettings.");
                        continue;
                    }

                    if (!enabled)
                    {
                        failures.Add($"{label}: scene '{scenePath}' is in EditorBuildSettings but disabled.");
                    }
                }
            }

            if (failures.Count > 0)
            {
                Assert.Fail("Planet scene wiring is out of sync with Build Settings:\n  - "
                    + string.Join("\n  - ", failures));
            }
        }

        [Test]
        public void EverySceneCatalogPlanetEntry_PointsAtBuildSettingsEntry()
        {
            var failures = new List<string>();
            var buildScenes = LoadBuildSettingsScenePaths();

            foreach (var catalog in LoadAllAssets<GameSceneCatalog>())
            {
                if (catalog == null || catalog.AllScenes == null) continue;

                for (var i = 0; i < catalog.AllScenes.Count; i++)
                {
                    var def = catalog.AllScenes[i];
                    if (def == null || def.Role != GameSceneRole.Planet) continue;
                    if (!def.IsConfigured) continue;

                    var scenePath = def.ScenePath;
                    var label = $"{catalog.name} -> {def.name}";

                    if (!File.Exists(scenePath))
                    {
                        failures.Add($"{label}: scene file does not exist at '{scenePath}'.");
                        continue;
                    }

                    if (!buildScenes.TryGetValue(scenePath, out var enabled))
                    {
                        failures.Add($"{label}: scene '{scenePath}' is not in EditorBuildSettings.");
                        continue;
                    }

                    if (!enabled)
                    {
                        failures.Add($"{label}: scene '{scenePath}' is in EditorBuildSettings but disabled.");
                    }
                }
            }

            if (failures.Count > 0)
            {
                Assert.Fail("Scene catalog Planet entries are out of sync with Build Settings:\n  - "
                    + string.Join("\n  - ", failures));
            }
        }

        [Test]
        public void EveryCatalogPlanetWithScene_HasRoundReadyEnvironment()
        {
            var failures = new List<string>();

            foreach (var catalog in LoadAllAssets<PlanetCatalog>())
            {
                if (catalog == null || catalog.AllPlanets == null) continue;

                for (var i = 0; i < catalog.AllPlanets.Count; i++)
                {
                    var planet = catalog.AllPlanets[i];
                    if (planet == null || !planet.HasPlanetScene) continue;

                    WithScene(planet.PlanetScene.ScenePath, scene =>
                    {
                        var environments = GetComponentsInScene<PlanetEnvironment>(scene);
                        var environment = FindCompatibleEnvironment(environments, planet);
                        var label = $"{catalog.name} -> {planet.name} -> {planet.PlanetScene.name}";

                        if (environment == null)
                        {
                            failures.Add($"{label}: no compatible PlanetEnvironment was found.");
                            return;
                        }

                        if (environment.LaunchpadZone == null)
                            failures.Add($"{label}: PlanetEnvironment '{environment.name}' has no LaunchpadZone assigned.");

                        if (!HasAnyLiveTransform(environment.PlayerSpawnPoints))
                            failures.Add($"{label}: PlanetEnvironment '{environment.name}' has no live player spawn points.");

                        if (!HasTeleporterToShip(scene))
                            failures.Add($"{label}: scene has no TeleporterPad targeting Ship.");
                    });
                }
            }

            if (failures.Count > 0)
            {
                Assert.Fail("Planet scenes are not round-ready:\n  - "
                    + string.Join("\n  - ", failures));
            }
        }

        [Test]
        public void BootstrapScene_HasShipTeleporterToActivePlanet()
        {
            const string bootstrapScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
            if (!File.Exists(bootstrapScenePath))
                Assert.Fail($"Bootstrap scene does not exist at '{bootstrapScenePath}'.");

            WithScene(bootstrapScenePath, scene =>
            {
                Assert.IsTrue(HasTeleporterToActivePlanet(scene),
                    "The bootstrap ship scene should include a TeleporterPad targeting ActivePlanet.");
            });
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
                // Last write wins on duplicates. That is fine here because the validation
                // only needs to catch a scene missing from build settings or disabled there.
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

            // Current tier 2 variants intentionally share one Rusty Moon scene. Runtime
            // binding allows same-tier environments as that fallback, so validation does too.
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
    }
}
