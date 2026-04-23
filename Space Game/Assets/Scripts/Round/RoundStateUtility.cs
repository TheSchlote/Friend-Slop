namespace FriendSlop.Round
{
    public static class RoundStateUtility
    {
        public static bool AreAllShipPartsInstalled(bool hasCockpit, bool hasWings, bool hasEngine)
        {
            return hasCockpit && hasWings && hasEngine;
        }

        public static bool IsLaunchReady(bool rocketAssembled, int boardedPlayerCount, int connectedPlayerCount)
        {
            if (!rocketAssembled || connectedPlayerCount <= 0)
            {
                return false;
            }

            return boardedPlayerCount >= connectedPlayerCount;
        }

        public static string FormatPartStatus(bool installed, string label)
        {
            return installed ? $"{label} OK" : $"{label} missing";
        }
    }
}
