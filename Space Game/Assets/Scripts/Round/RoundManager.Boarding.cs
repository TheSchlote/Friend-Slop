namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        public void ServerPlayerBoarded(ulong clientId)
        {
            if (!IsServer || Phase.Value != RoundPhase.Active)
                return;

            if (boardedPlayerIds.Add(clientId))
            {
                UpdateBoardedPlayerCount();
                CheckLaunchCondition();
            }
        }

        public void ServerPlayerUnboarded(ulong clientId)
        {
            if (!IsServer)
                return;

            if (boardedPlayerIds.Remove(clientId))
            {
                UpdateBoardedPlayerCount();
            }
        }

        private void CheckLaunchCondition()
        {
            var objective = ActiveObjective;
            if (objective != null)
            {
                if (Phase.Value != RoundPhase.Active) return;
                var status = objective.Evaluate(this);
                if (status == ObjectiveStatus.Success) ServerSetPhase(RoundPhase.Success);
                else if (status == ObjectiveStatus.Failed) ServerSetPhase(RoundPhase.Failed);
                return;
            }

            var connectedPlayerCount = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            if (!RoundStateUtility.IsLaunchReady(RocketAssembled.Value, PlayersBoarded.Value, connectedPlayerCount))
                return;

            ServerSetPhase(RoundPhase.Success);
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;

            if (Phase.Value == RoundPhase.Loading)
            {
                var wasReady = _readyPlayerIds.Remove(clientId);
                var loadingCounts = RoundStateUtility.RemoveDisconnectedLoadingPlayer(
                    PlayersExpectedToLoad.Value,
                    PlayersReady.Value,
                    wasReady);
                PlayersExpectedToLoad.Value = loadingCounts.ExpectedToLoad;
                PlayersReady.Value = loadingCounts.ReadyCount;

                if (PlayersExpectedToLoad.Value <= 0 || PlayersReady.Value >= PlayersExpectedToLoad.Value)
                {
                    ServerSetPhase(RoundPhase.Active);
                }
            }

            if (boardedPlayerIds.Remove(clientId))
            {
                UpdateBoardedPlayerCount();
                CheckLaunchCondition();
            }
        }

        private void UpdateBoardedPlayerCount()
        {
            if (NetworkManager == null)
            {
                PlayersBoarded.Value = boardedPlayerIds.Count;
                return;
            }

            var connectedBoardedPlayers = 0;
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                if (boardedPlayerIds.Contains(clientId))
                {
                    connectedBoardedPlayers++;
                }
            }

            PlayersBoarded.Value = connectedBoardedPlayers;
        }
    }
}
