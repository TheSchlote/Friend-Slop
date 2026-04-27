using FriendSlop.SceneManagement;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    public class GameScenePathUtilityTests
    {
        [TestCase(@"Assets\Scenes\ShipInterior.unity", "Assets/Scenes/ShipInterior.unity")]
        [TestCase(" Assets/Scenes/Planet_Starter.unity ", "Assets/Scenes/Planet_Starter.unity")]
        [TestCase("", "")]
        public void NormalizePath_TrimsAndUsesForwardSlashes(string input, string expected)
        {
            Assert.AreEqual(expected, GameScenePathUtility.NormalizePath(input));
        }

        [TestCase("Assets/Scenes/ShipInterior.unity", true)]
        [TestCase("Assets/Scenes/Planet_Starter.UNITY", true)]
        [TestCase("Scenes/ShipInterior.unity", false)]
        [TestCase("Assets/Scenes/ShipInterior", false)]
        public void IsUnityScenePath_RequiresAssetsUnityPath(string input, bool expected)
        {
            Assert.AreEqual(expected, GameScenePathUtility.IsUnityScenePath(input));
        }

        [TestCase("Assets/Scenes/ShipInterior.unity", "ShipInterior")]
        [TestCase(@"Assets\Planets\Planet_Starter.unity", "Planet_Starter")]
        public void GetSceneName_UsesPathFileName(string input, string expected)
        {
            Assert.AreEqual(expected, GameScenePathUtility.GetSceneName(input));
        }
    }
}
