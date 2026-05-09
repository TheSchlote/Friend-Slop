namespace FriendSlop.Round
{
    public static class RoundStateUtility
    {
        public static bool AreAllShipPartsInstalled(bool hasCockpit, bool hasWings, bool hasEngine)
        {
            return hasCockpit && hasWings && hasEngine;
        }

        public static (int ExpectedToLoad, int ReadyCount) RemoveDisconnectedLoadingPlayer(
            int expectedToLoad,
            int readyCount,
            bool playerWasReady)
        {
            if (expectedToLoad > 0)
            {
                expectedToLoad--;
            }

            if (playerWasReady && readyCount > 0)
            {
                readyCount--;
            }

            if (readyCount > expectedToLoad)
            {
                readyCount = expectedToLoad;
            }

            if (readyCount < 0)
            {
                readyCount = 0;
            }

            return (expectedToLoad, readyCount);
        }

        public static bool IsLaunchReady(bool rocketAssembled, int boardedPlayerCount, int connectedPlayerCount)
        {
            if (!rocketAssembled || connectedPlayerCount <= 0)
            {
                return false;
            }

            return boardedPlayerCount >= connectedPlayerCount;
        }

        public static bool IsShipPhase(RoundPhase phase)
        {
            return phase == RoundPhase.Lobby
                || phase == RoundPhase.Success
                || phase == RoundPhase.Failed
                || phase == RoundPhase.AllDead;
        }

        public static bool AllowsGameplayInput(RoundPhase phase)
        {
            return phase == RoundPhase.Active || IsShipPhase(phase);
        }

        public static string FormatPartStatus(bool installed, string label)
        {
            return installed ? $"{label} OK" : $"{label} missing";
        }
    }
}
