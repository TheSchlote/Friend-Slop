using System.Reflection;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Tests.EditMode
{
    public class RoundManagerPlanetTravelCleanupTests
    {
        private const string TempSceneFolder = "Assets/__TempTests";
        private const string PlanetScenePath = TempSceneFolder + "/PlanetTravelCleanupPlanet.unity";
        private const string ShipScenePath = TempSceneFolder + "/PlanetTravelCleanupShip.unity";

        private Scene planetScene;
        private Scene shipScene;
        private GameObject activeEnvironmentObject;
        private GameObject planetActor;
        private GameObject shipActor;

        [TearDown]
        public void TearDown()
        {
            DestroyIfExists(planetActor);
            DestroyIfExists(shipActor);
            DestroyIfExists(activeEnvironmentObject);

            if ((planetScene.IsValid() && planetScene.isLoaded)
                || (shipScene.IsValid() && shipScene.isLoaded))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            AssetDatabase.DeleteAsset(PlanetScenePath);
            AssetDatabase.DeleteAsset(ShipScenePath);
            AssetDatabase.DeleteAsset(TempSceneFolder);
        }

        [Test]
        public void PlanetTravelCleanupScope_WhenActiveEnvironmentKnown_OnlyIncludesActivePlanetScene()
        {
            CreateSceneFixture();
            var env = activeEnvironmentObject.AddComponent<PlanetEnvironment>();

            Assert.IsTrue(ShouldCleanupActorForPlanetTravel(planetActor, env),
                "Planet-owned actors should be cleaned up when leaving the active planet.");
            Assert.IsFalse(ShouldCleanupActorForPlanetTravel(shipActor, env),
                "Ship/bootstrap actors should survive planet travel cleanup once a planet scene owns the round actors.");
        }

        [Test]
        public void PlanetTravelCleanupScope_WhenEnvironmentMissing_KeepsLegacyGlobalCleanup()
        {
            CreateSceneFixture();

            Assert.IsTrue(ShouldCleanupActorForPlanetTravel(planetActor, null));
            Assert.IsTrue(ShouldCleanupActorForPlanetTravel(shipActor, null));
        }

        private void CreateSceneFixture()
        {
            if (!AssetDatabase.IsValidFolder(TempSceneFolder))
                AssetDatabase.CreateFolder("Assets", "__TempTests");

            planetScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(EditorSceneManager.SaveScene(planetScene, PlanetScenePath));

            shipScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Assert.IsTrue(EditorSceneManager.SaveScene(shipScene, ShipScenePath));

            activeEnvironmentObject = new GameObject("Active Planet Environment");
            planetActor = new GameObject("Planet Actor");
            shipActor = new GameObject("Ship Actor");

            SceneManager.MoveGameObjectToScene(activeEnvironmentObject, planetScene);
            SceneManager.MoveGameObjectToScene(planetActor, planetScene);
            SceneManager.MoveGameObjectToScene(shipActor, shipScene);
        }

        private static bool ShouldCleanupActorForPlanetTravel(GameObject actor, PlanetEnvironment activeEnv)
        {
            var method = typeof(RoundManager).GetMethod("ShouldCleanupActorForPlanetTravel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "RoundManager should keep planet-travel cleanup scope testable.");
            return (bool)method.Invoke(null, new object[] { actor, activeEnv });
        }

        private static void DestroyIfExists(GameObject target)
        {
            if (target != null)
                Object.DestroyImmediate(target);
        }
    }
}
