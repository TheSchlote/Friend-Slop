using System.Reflection;
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
        private const string SharedTier2ScenePath = "Assets/Scenes/Planet_RustyMoon.unity";

        [Test]
        public void Tier2Variants_UseExplicitObjectivesAndSharedRustyMoonScene()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            var rustyMoon = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(RustyMoonPath);
            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");
            Assert.IsNotNull(rustyMoon, $"Expected shared Tier 2 scene owner at {RustyMoonPath}.");
            Assert.IsTrue(rustyMoon.HasPlanetScene, "Rusty Moon should own the shared Tier 2 planet scene.");
            Assert.AreEqual(SharedTier2ScenePath, rustyMoon.PlanetScene.ScenePath);

            AssertVariantUsesSharedScene(catalog, rustyMoon, DeepHaulPath);
            AssertVariantUsesSharedScene(catalog, rustyMoon, QuickStrikePath);
            AssertVariantUsesSharedScene(catalog, rustyMoon, GhostShiftPath);
        }

        private static void AssertVariantUsesSharedScene(PlanetCatalog catalog, PlanetDefinition rustyMoon, string assetPath)
        {
            var variant = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(assetPath);
            Assert.IsNotNull(variant, $"Expected Tier 2 variant at {assetPath}.");
            Assert.AreEqual(2, variant.Tier, $"{variant.name} should stay in tier 2.");
            Assert.IsFalse(variant.HasPlanetScene,
                $"{variant.name} is a mission variant and should fall back to Rusty Moon's shared scene.");
            Assert.IsNotNull(variant.Objective,
                $"{variant.name} should declare an explicit objective because it shares scene content.");

            var owner = ResolveSceneOwner(variant, catalog);
            Assert.AreSame(rustyMoon, owner,
                $"{variant.name} should resolve to Rusty Moon as its scene owner.");
        }

        private static PlanetDefinition ResolveSceneOwner(PlanetDefinition planet, PlanetCatalog catalog)
        {
            var method = typeof(PlanetSceneOrchestrator).GetMethod("ResolveSceneOwner",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "PlanetSceneOrchestrator should expose a private scene-owner resolver.");
            return (PlanetDefinition)method.Invoke(null, new object[] { planet, catalog });
        }
    }
}
