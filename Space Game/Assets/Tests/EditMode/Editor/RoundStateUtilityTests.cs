using FriendSlop.Round;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    public class RoundStateUtilityTests
    {
        [TestCase(true, true, true, true)]
        [TestCase(false, true, true, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, false, false)]
        [TestCase(false, false, false, false)]
        public void AreAllShipPartsInstalled_RequiresEveryUniquePart(bool cockpit, bool wings, bool engine, bool expected)
        {
            Assert.AreEqual(expected, RoundStateUtility.AreAllShipPartsInstalled(cockpit, wings, engine));
        }

        [TestCase(true, 1, 1, true)]
        [TestCase(true, 4, 4, true)]
        [TestCase(true, 1, 2, false)]
        [TestCase(false, 4, 4, false)]
        [TestCase(true, 0, 0, false)]
        public void IsLaunchReady_RequiresAssembledRocketAndEveryConnectedPlayerOnPad(bool rocketAssembled, int boardedPlayers, int connectedPlayers, bool expected)
        {
            Assert.AreEqual(expected, RoundStateUtility.IsLaunchReady(rocketAssembled, boardedPlayers, connectedPlayers));
        }

        [TestCase(true, "Cockpit", "Cockpit OK")]
        [TestCase(false, "Engine", "Engine missing")]
        public void FormatPartStatus_ReturnsPlayerFacingProgress(bool installed, string label, string expected)
        {
            Assert.AreEqual(expected, RoundStateUtility.FormatPartStatus(installed, label));
        }

        [TestCase(3, 2, true, 2, 1)]
        [TestCase(2, 1, false, 1, 1)]
        [TestCase(1, 1, true, 0, 0)]
        [TestCase(0, 0, false, 0, 0)]
        public void RemoveDisconnectedLoadingPlayer_ClampsExpectedAndReadyCounts(
            int expectedToLoad,
            int readyCount,
            bool playerWasReady,
            int expectedExpectedToLoad,
            int expectedReadyCount)
        {
            var result = RoundStateUtility.RemoveDisconnectedLoadingPlayer(expectedToLoad, readyCount, playerWasReady);

            Assert.AreEqual(expectedExpectedToLoad, result.ExpectedToLoad);
            Assert.AreEqual(expectedReadyCount, result.ReadyCount);
        }
    }
}
