using System.Linq;
using FriendSlop.Hazards;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Pins the BACKLOG §9 PlanetHazardSet resolver contract. The AnomalySpawner
    // calls ResolveAnomalyPrefabs(planet, globalDefault) every spawn tick — these
    // tests freeze the four decision branches (null planet, hard suppress, no
    // hazard set, hazard set present) so a regression there surfaces here, not in
    // a playtest where it presents as "anomalies stopped spawning on Wraith Halo".
    public class PlanetHazardSetTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static GameObject MakePrefab(string name)
        {
            // GameObject with no NetworkObject is fine — the resolver only cares
            // about array identity / count, not what the prefab does.
            var go = new GameObject(name);
            return go;
        }

        private static PlanetHazardSet MakeHazardSet(params GameObject[] anomalyPrefabs)
        {
            var set = ScriptableObject.CreateInstance<PlanetHazardSet>();
            var so  = new UnityEditor.SerializedObject(set);
            var arr = so.FindProperty("anomalyPrefabs");
            arr.arraySize = anomalyPrefabs.Length;
            for (int i = 0; i < anomalyPrefabs.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = anomalyPrefabs[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return set;
        }

        private static PlanetDefinition MakePlanet(
            bool suppressAnomalies = false,
            PlanetHazardSet hazardSet = null)
        {
            var planet = ScriptableObject.CreateInstance<PlanetDefinition>();
            var so = new UnityEditor.SerializedObject(planet);
            so.FindProperty("suppressAnomalies").boolValue = suppressAnomalies;
            so.FindProperty("hazardSet").objectReferenceValue = hazardSet;
            so.ApplyModifiedPropertiesWithoutUndo();
            return planet;
        }

        // ── SO accessors ───────────────────────────────────────────────────────

        [Test]
        public void AnomalyPrefabs_ReturnsEmptyForFreshInstance()
        {
            var set = ScriptableObject.CreateInstance<PlanetHazardSet>();
            Assert.AreEqual(0, set.AnomalyPrefabs.Count);
            Assert.AreEqual(0, set.MonsterPrefabs.Count);
        }

        [Test]
        public void AnomalyPrefabs_ReturnsAuthoredEntries()
        {
            var a = MakePrefab("A");
            var b = MakePrefab("B");
            var set = MakeHazardSet(a, b);

            CollectionAssert.AreEqual(new[] { a, b }, set.AnomalyPrefabs.ToList());
        }

        // ── Resolver ───────────────────────────────────────────────────────────

        [Test]
        public void Resolve_NullPlanet_ReturnsGlobalDefault()
        {
            var globalA = MakePrefab("GlobalA");
            var globalB = MakePrefab("GlobalB");
            var global = new[] { globalA, globalB };

            var result = PlanetHazardSet.ResolveAnomalyPrefabs(null, global);

            CollectionAssert.AreEqual(global, result.ToList());
        }

        [Test]
        public void Resolve_NullPlanet_NullDefault_ReturnsEmpty()
        {
            var result = PlanetHazardSet.ResolveAnomalyPrefabs(null, null);

            Assert.IsNotNull(result, "Resolver must not return null");
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Resolve_SuppressAnomalies_ReturnsEmpty_EvenWhenHazardSetHasEntries()
        {
            var setEntry = MakePrefab("InSet");
            var set = MakeHazardSet(setEntry);
            var planet = MakePlanet(suppressAnomalies: true, hazardSet: set);

            var globalDefault = new[] { MakePrefab("Global") };
            var result = PlanetHazardSet.ResolveAnomalyPrefabs(planet, globalDefault);

            Assert.AreEqual(0, result.Count, "Hard suppression must beat the hazard-set override");
        }

        [Test]
        public void Resolve_NoHazardSet_FallsBackToGlobalDefault()
        {
            var globalA = MakePrefab("GlobalA");
            var planet = MakePlanet(suppressAnomalies: false, hazardSet: null);

            var result = PlanetHazardSet.ResolveAnomalyPrefabs(planet, new[] { globalA });

            CollectionAssert.AreEqual(new[] { globalA }, result.ToList());
        }

        [Test]
        public void Resolve_HazardSetWithEntries_OverridesGlobalDefault()
        {
            var setA = MakePrefab("SetA");
            var setB = MakePrefab("SetB");
            var set = MakeHazardSet(setA, setB);
            var planet = MakePlanet(hazardSet: set);

            var globalDefault = new[] { MakePrefab("Global") };
            var result = PlanetHazardSet.ResolveAnomalyPrefabs(planet, globalDefault);

            CollectionAssert.AreEqual(new[] { setA, setB }, result.ToList(),
                "Hazard-set entries replace, not append to, the global default");
        }

        [Test]
        public void Resolve_HazardSetWithEmptyAnomalies_SuppressesViaData()
        {
            var set = MakeHazardSet(); // explicitly empty
            var planet = MakePlanet(hazardSet: set);

            var globalDefault = new[] { MakePrefab("Global") };
            var result = PlanetHazardSet.ResolveAnomalyPrefabs(planet, globalDefault);

            Assert.AreEqual(0, result.Count,
                "Planet authored 'hazardSet with no anomalies' = explicit no-anomalies");
        }

        [Test]
        public void PlanetDefinition_HazardSetGetter_RoundTrips()
        {
            var set = ScriptableObject.CreateInstance<PlanetHazardSet>();
            var planet = MakePlanet(hazardSet: set);

            Assert.AreSame(set, planet.HazardSet);
        }
    }
}
