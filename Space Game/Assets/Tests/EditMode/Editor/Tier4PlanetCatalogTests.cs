using FriendSlop.Round;
using NUnit.Framework;
using UnityEditor;

namespace FriendSlop.Tests.EditMode
{
    // Tier 4 is the procgen sandbox: a single test-mode-only planet that owns its
    // scene. These assertions catch the failure modes that would silently leak it
    // into normal progression or detach it from its scene during a refactor.
    public class Tier4PlanetCatalogTests
    {
        private const string CatalogPath = "Assets/Planets/PlanetCatalog.asset";
        private const string PlanetPath = "Assets/Planets/Tier4_HillsAndValleys.asset";
        private const string ScenePath = "Assets/Scenes/Planet_HillsAndValleys.unity";

        [Test]
        public void Tier4_HillsAndValleys_OwnsItsSceneAndIsTestModeOnly()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            var planet = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(PlanetPath);
            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");
            Assert.IsNotNull(planet, $"Expected Tier 4 planet at {PlanetPath}.");

            Assert.AreEqual(4, planet.Tier);
            Assert.IsTrue(planet.HasPlanetScene, "Tier 4 should own its own scene.");
            Assert.AreEqual(ScenePath, planet.PlanetScene.ScenePath);
            Assert.IsTrue(planet.IsTestModeOnly,
                "Tier 4 is currently a procgen sandbox; flip testModeOnly=false when ready to ship for normal progression.");

            // Test-mode-only planets must be excluded from tier-progression rolls so
            // a normal tier-3 success doesn't accidentally route players into the
            // sandbox before the generator is dialed in.
            var tier4Pool = catalog.GetPlanetsForTier(4);
            CollectionAssert.DoesNotContain(tier4Pool, planet,
                "Test-mode-only Tier 4 must not appear in GetPlanetsForTier rolls.");

            Assert.GreaterOrEqual(catalog.IndexOf(planet), 0,
                "Tier 4 must still be in the catalog so Test Mode can reach it via GetByIndex.");
        }
    }
}
