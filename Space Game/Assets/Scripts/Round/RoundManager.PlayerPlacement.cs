using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        private void RespawnPlayersAtPlanet()
        {
            if (playerSpawnPoints == null || playerSpawnPoints.Length == 0)
                return;

            for (var index = 0; index < NetworkFirstPersonController.ActivePlayers.Count; index++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[index];
                if (player == null || !player.IsSpawned)
                    continue;

                var spawn = GetSpawnForPlayer(player, playerSpawnPoints);
                if (spawn == null)
                    continue;
                player.ServerTeleport(spawn.position, spawn.rotation);
                player.ServerRevive();
            }
        }

        public void ServerPlaceNewPlayer(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null)
                return;

            var spawnPoints = RoundStateUtility.IsShipPhase(Phase.Value) ? shipSpawnPoints : playerSpawnPoints;
            if (spawnPoints == null || spawnPoints.Length == 0)
                return;

            var spawn = GetSpawnForPlayer(player, spawnPoints);
            if (spawn == null) return;
            player.ServerTeleport(spawn.position, spawn.rotation);
        }

        // Teleporter-pad entry points explicitly target the ship or active planet instead
        // of using phase-driven placement for newly joined players.
        public bool ServerTeleportPlayerToShip(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null) return false;
            return TeleportToSpawnPoints(player, shipSpawnPoints);
        }

        public bool ServerTeleportPlayerToPlanet(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null) return false;
            return TeleportToSpawnPoints(player, playerSpawnPoints);
        }

        private static bool TeleportToSpawnPoints(NetworkFirstPersonController player, Transform[] spawns)
        {
            if (spawns == null || spawns.Length == 0) return false;
            var spawn = GetSpawnForPlayer(player, spawns);
            if (spawn == null) return false;
            player.ServerTeleport(spawn.position, spawn.rotation);
            return true;
        }

        public void ServerNotifyTeleporterEffect(NetworkFirstPersonController player, Vector3 padPosition)
        {
            if (!IsServer || player == null) return;

            var flashParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { player.OwnerClientId } }
            };
            TeleporterFlashClientRpc(flashParams);
            TeleporterSoundClientRpc(padPosition);
        }

        [ClientRpc]
        private void TeleporterFlashClientRpc(ClientRpcParams rpcParams = default)
        {
            LocalTeleporterFlashRequested?.Invoke();
        }

        [ClientRpc]
        private void TeleporterSoundClientRpc(Vector3 padPosition)
        {
            TeleporterAudio.PlayAt(padPosition);
        }

        private void ServerMovePlayersToShip(bool revivePlayers)
        {
            if (!IsServer || shipSpawnPoints == null || shipSpawnPoints.Length == 0)
                return;

            for (var index = 0; index < NetworkFirstPersonController.ActivePlayers.Count; index++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[index];
                if (player == null || !player.IsSpawned)
                    continue;

                var spawn = GetSpawnForPlayer(player, shipSpawnPoints);
                if (spawn == null) continue;
                if (revivePlayers)
                    player.ServerRevive();
                player.ServerTeleport(spawn.position, spawn.rotation);
            }
        }

        private static Transform GetSpawnForPlayer(NetworkFirstPersonController player, Transform[] spawnPoints)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;
            var startIndex = NetworkFirstPersonController.ActivePlayers.IndexOf(player);
            if (startIndex < 0) startIndex = 0;

            for (var offset = 0; offset < spawnPoints.Length; offset++)
            {
                var candidate = spawnPoints[(startIndex + offset) % spawnPoints.Length];
                if (candidate != null) return candidate;
            }
            return null;
        }
    }
}
