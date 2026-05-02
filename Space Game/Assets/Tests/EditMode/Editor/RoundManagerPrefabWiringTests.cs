using FriendSlop.Round;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class RoundManagerPrefabWiringTests
    {
        private const string RoundManagerPrefabPath = "Assets/Prefabs/RoundManager.prefab";

        [Test]
        public void RoundManagerPrefab_WiresPlanetSceneOrchestrator()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath);
            Assert.IsNotNull(prefab, $"Expected RoundManager prefab at {RoundManagerPrefabPath}.");

            var round = prefab.GetComponent<RoundManager>();
            var orchestrator = prefab.GetComponent<PlanetSceneOrchestrator>();
            Assert.IsNotNull(round, "RoundManager prefab should include RoundManager.");
            Assert.IsNotNull(orchestrator, "RoundManager prefab should include PlanetSceneOrchestrator.");

            var serializedRound = new SerializedObject(round);
            var property = serializedRound.FindProperty("planetSceneOrchestrator");
            Assert.IsNotNull(property, "RoundManager should expose a serialized planetSceneOrchestrator field.");
            Assert.AreSame(orchestrator, property.objectReferenceValue,
                "RoundManager should delegate planet scene loading to the orchestrator on the same prefab.");
        }
    }
}
