using FriendSlop.Round;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class PlanetCatalogProgressionTests
    {
        private const string CatalogPath = "Assets/Planets/PlanetCatalog.asset";

        [Test]
        public void HighestAuthoredTier_UsesCatalogContentRatherThanHardLimit()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);

            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");
            Assert.AreEqual(3, catalog.HighestAuthoredTier,
                "Authored progression should currently end at the highest catalog planet tier.");
            Assert.Less(catalog.HighestAuthoredTier, PlanetCatalog.MaxTier,
                "This test guards against accidentally treating placeholder max tiers as authored content.");
        }

        [Test]
        public void RoundManager_FinalTierTracksHighestAuthoredCatalogTier()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlanetCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, $"Expected planet catalog at {CatalogPath}.");

            var roundObject = new GameObject("Round Manager Progression Test");
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                var serializedRound = new SerializedObject(round);
                serializedRound.FindProperty("planetCatalog").objectReferenceValue = catalog;
                serializedRound.ApplyModifiedPropertiesWithoutUndo();

                round.CurrentTier.Value = catalog.HighestAuthoredTier;

                Assert.AreEqual(catalog.HighestAuthoredTier, round.FinalTier);
                Assert.AreEqual(catalog.HighestAuthoredTier, round.NextTier);
                Assert.IsTrue(round.HasReachedFinalTier);
            }
            finally
            {
                Object.DestroyImmediate(roundObject);
            }
        }
    }
}
