using System.Collections.Generic;
using System.IO;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using NUnit.Framework;
using UnityEditor;

namespace FriendSlop.Tests.EditMode
{
    // Catches the "I added a planet scene but forgot to drag it into Build Settings"
    // failure mode at test time. Without this, the runtime additive load just silently
    // returns InvalidSceneName / SceneManagementNotEnabled and the host hangs in the
    // Lobby phase waiting for an env that will never load.
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
                    var label = $"{catalog.name} → {planet.name} → {planet.PlanetScene.name}";

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
                    var label = $"{catalog.name} → {def.name}";

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

        private static Dictionary<string, bool> LoadBuildSettingsScenePaths()
        {
            var result = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
            var scenes = EditorBuildSettings.scenes;
            for (var i = 0; i < scenes.Length; i++)
            {
                var entry = scenes[i];
                if (entry == null || string.IsNullOrEmpty(entry.path)) continue;
                var normalized = GameScenePathUtility.NormalizePath(entry.path);
                // Last write wins on duplicates - matches Unity's behavior of using the
                // first enabled entry, but the dictionary only flags an error when *all*
                // entries are disabled, which is what we care about here.
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
    }
}
