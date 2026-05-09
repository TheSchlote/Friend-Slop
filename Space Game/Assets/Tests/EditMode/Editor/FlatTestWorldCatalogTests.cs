using System.Collections.Generic;
using System.IO;
using System.Linq;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Lock down the contract that keeps the flat test world reachable through Test Mode
    // but invisible to normal tier progression. If any of these assertions fail the game
    // either silently dropped the test world from the picker or, worse, surfaced it to
    // players as a real next-tier option.
    public class FlatTestWorldCatalogTests
    {
        private const string CatalogPath = "Assets/Planets/PlanetCatalog.asset";
        private const string FlatTestWorldAssetPath = "Assets/Planets/TestWorld_Flat.asset";

        [Test]
        public void TestWorldFlatAsset_IsFlatTestWorldAndTestModeOnly()
        {
            var planet = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(FlatTestWorldAssetPath);
            Assert.IsNotNull(planet, $"Expected flat test world asset at {FlatTestWorldAssetPath}.");
            Assert.IsTrue(planet.IsFlatTestWorld, "TestWorld_Flat should be flagged flatTestWorld.");
            Assert.IsTrue(planet.IsTestModeOnly, "Flat test worlds must be test-mode-only.");
            Assert.IsFalse(planet.HasPlanetScene,
                "Flat test world is built procedurally at runtime; it should not reference a scene file.");
        }

        [Test]
        public void Catalog_IncludesFlatTestWorld_ButHidesItFromTierLookups()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            var flatTestWorld = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(FlatTestWorldAssetPath);
            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");
            Assert.IsNotNull(flatTestWorld, $"Expected flat test world asset at {FlatTestWorldAssetPath}.");

            var indexInCatalog = catalog.IndexOf(flatTestWorld);
            Assert.GreaterOrEqual(indexInCatalog, 0,
                "Flat test world must be in the catalog so the Test Mode picker can reach it via GetByIndex.");

            // Test Mode iterates AllPlanets directly, so an unfiltered list contains it.
            CollectionAssert.Contains(catalog.AllPlanets, flatTestWorld,
                "Test Mode picker reads catalog.AllPlanets - flat test world must appear there.");

            // Normal tier progression goes through GetPlanetsForTier / GetFirstForTier; the
            // test world must be excluded from both regardless of which tier number it lives
            // at, so future authors can put it on any tier without leaking it into rotation.
            var tierList = catalog.GetPlanetsForTier(flatTestWorld.Tier);
            CollectionAssert.DoesNotContain(tierList, flatTestWorld,
                "GetPlanetsForTier must exclude test-mode-only planets from progression rolls.");

            var firstForTier = catalog.GetFirstForTier(flatTestWorld.Tier);
            Assert.AreNotSame(flatTestWorld, firstForTier,
                "GetFirstForTier must skip test-mode-only planets so RoundManager's startup fallback never lands on one.");
        }

        [Test]
        public void FlatTestWorld_ReferencesDisplaySetCoveringEveryLootAndHazardPrefab()
        {
            var planet = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(FlatTestWorldAssetPath);
            Assert.IsNotNull(planet, $"Expected flat test world asset at {FlatTestWorldAssetPath}.");
            Assert.IsNotNull(planet.DisplaySet,
                "Flat test world must reference a TestWorldDisplaySet so the showcase has something to spawn.");

            // Every prefab under these directories should appear in the showcase. If someone
            // adds a new loot or hazard prefab and forgets to wire it into the display set,
            // this test catches it - the whole point of the test world is "everything is here".
            var expected = new[]
            {
                "Assets/Prefabs/Loot",
                "Assets/Prefabs/PoolLoot",
                "Assets/Prefabs/Anomalies",
            }
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.prefab", SearchOption.TopDirectoryOnly))
                .Select(path => path.Replace('\\', '/'))
                .ToList();
            // RoamingMonster lives at the prefabs root rather than under a category folder,
            // so add it explicitly. Adjust this list if the project grows top-level hazard prefabs.
            const string roamingMonsterPath = "Assets/Prefabs/RoamingMonster.prefab";
            if (File.Exists(roamingMonsterPath)) expected.Add(roamingMonsterPath);

            var displayedNames = new HashSet<string>(
                planet.DisplaySet.AllPrefabs()
                    .Where(p => p != null)
                    .Select(p => p.name));

            var missing = new List<string>();
            foreach (var prefabPath in expected)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;
                if (!displayedNames.Contains(prefab.name))
                    missing.Add(prefab.name);
            }

            Assert.IsEmpty(missing,
                "TestWorldDisplaySet is missing prefabs that exist on disk - wire them into the asset:\n  - "
                + string.Join("\n  - ", missing));
        }

        [Test]
        public void FlatTestWorld_DoesNotShadowOtherTierSceneOwners()
        {
            // Each tier 2 variant now owns its own scene, so PlanetSceneOwnership.ResolveSceneOwner
            // should return the variant itself. Adding a no-scene flat test world to the catalog
            // must not change that resolution.
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");

            var deepHaul = AssetDatabase.LoadAssetAtPath<PlanetDefinition>("Assets/Planets/Tier2_DeepHaul.asset");
            Assert.IsNotNull(deepHaul, "Expected Tier2_DeepHaul asset.");

            var owner = PlanetSceneOwnership.ResolveSceneOwner(deepHaul, catalog);
            Assert.AreSame(deepHaul, owner,
                "Tier 2 variants should resolve to themselves as their scene owner after the flat test world was added.");
        }
    }
}
