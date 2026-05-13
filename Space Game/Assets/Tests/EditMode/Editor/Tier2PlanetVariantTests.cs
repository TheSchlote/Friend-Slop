using FriendSlop.Round;
using NUnit.Framework;
using UnityEditor;

namespace FriendSlop.Tests.EditMode
{
    public class Tier2PlanetVariantTests
    {
        private const string CatalogPath = "Assets/Planets/PlanetCatalog.asset";
        private const string RustyMoonPath = "Assets/Planets/Tier2_RustyMoon.asset";
        private const string DeepHaulPath = "Assets/Planets/Tier2_DeepHaul.asset";
        private const string QuickStrikePath = "Assets/Planets/Tier2_QuickStrike.asset";
        private const string GhostShiftPath = "Assets/Planets/Tier2_GhostShift.asset";
        private const string RustyMoonScenePath = "Assets/Scenes/Planet_RustyMoon.unity";
        private const string DeepHaulScenePath = "Assets/Scenes/Planet_DeepHaul.unity";
        private const string QuickStrikeScenePath = "Assets/Scenes/Planet_QuickStrike.unity";
        private const string GhostShiftScenePath = "Assets/Scenes/Planet_GhostShift.unity";

        [Test]
        public void Tier2Variants_EachOwnTheirScene()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");

            AssertVariantOwnsScene(catalog, RustyMoonPath, RustyMoonScenePath);
            AssertVariantOwnsScene(catalog, DeepHaulPath, DeepHaulScenePath);
            AssertVariantOwnsScene(catalog, QuickStrikePath, QuickStrikeScenePath);
            AssertVariantOwnsScene(catalog, GhostShiftPath, GhostShiftScenePath);
        }

        [Test]
        public void Tier2Variants_DisplayAsMissionsOnSharedWorld()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            var rustyMoon = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(RustyMoonPath);
            var deepHaul = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(DeepHaulPath);

            var variantLabel = PlanetDisplayUtility.FormatPlanetLabel(deepHaul, catalog);
            var ownerLabel = PlanetDisplayUtility.FormatPlanetLabel(rustyMoon, catalog);

            StringAssert.Contains("mission on Rusty Moon", variantLabel);
            StringAssert.DoesNotContain("mission", ownerLabel);
        }

        private static void AssertVariantOwnsScene(PlanetCatalog catalog, string assetPath, string expectedScenePath)
        {
            var variant = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(assetPath);
            Assert.IsNotNull(variant, $"Expected Tier 2 variant at {assetPath}.");
            Assert.AreEqual(2, variant.Tier, $"{variant.name} should stay in tier 2.");
            Assert.IsTrue(variant.HasPlanetScene,
                $"{variant.name} should own its own planet scene now that variants are per-scene.");
            Assert.AreEqual(expectedScenePath, variant.PlanetScene.ScenePath,
                $"{variant.name} should reference {expectedScenePath}.");
            Assert.IsNotNull(variant.Objective,
                $"{variant.name} should declare an explicit objective per-variant.");

            var owner = PlanetSceneOwnership.ResolveSceneOwner(variant, catalog);
            Assert.AreSame(variant, owner,
                $"{variant.name} should resolve to itself as scene owner now that it has its own scene.");
        }
    }
}
