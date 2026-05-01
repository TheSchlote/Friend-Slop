using System.Collections.Generic;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public enum TeleporterTarget
    {
        // Sends the player to the active planet's spawn anchors (RoundManager.playerSpawnPoints).
        ActivePlanet = 0,
        // Sends the player to the ship-lobby spawn anchors (RoundManager.shipSpawnPoints).
        Ship = 1,
    }

    // Trigger-based pad that warps a player between the ship and the active planet during
    // the Active phase. Server authoritative; clients don't run any of this. A shared
    // per-player cooldown prevents the typical loop where the destination pad's trigger
    // fires the moment the player rematerializes on it.
    [RequireComponent(typeof(Collider))]
    public class TeleporterPad : MonoBehaviour
    {
        [SerializeField] private TeleporterTarget destination = TeleporterTarget.ActivePlanet;
        [SerializeField, Min(0f)] private float perPlayerCooldownSeconds = 2.5f;

        // Static so the cooldown carries across pads - otherwise stepping onto the
        // destination pad immediately bounces you back since each pad has its own timer.
        private static readonly Dictionary<ulong, float> NextAllowedTeleportTime = new();

        public TeleporterTarget Destination => destination;
        public float PerPlayerCooldownSeconds => perPlayerCooldownSeconds;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryTeleport(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // Re-check on stay so a player who walks on while on cooldown still gets
            // teleported the moment the cooldown clears, without needing to step off
            // and back on.
            TryTeleport(other);
        }

        private void TryTeleport(Collider other)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            var rm = RoundManager.Instance;
            if (rm == null || rm.Phase.Value != RoundPhase.Active) return;

            var player = other.GetComponentInParent<NetworkFirstPersonController>();
            if (player == null) return;
            if (player.IsDead || player.IsBeingCarried.Value) return;
            // Carrying another player would leave them stranded - the carry sync only
            // moves the held player when the carrier's normal movement updates, not on
            // a server teleport. Block until the carrier lets go.
            if (player.HasHeldPlayer) return;

            var clientId = player.OwnerClientId;
            if (NextAllowedTeleportTime.TryGetValue(clientId, out var readyAt) && Time.time < readyAt)
                return;

            var teleported = destination == TeleporterTarget.Ship
                ? rm.ServerTeleportPlayerToShip(player)
                : rm.ServerTeleportPlayerToPlanet(player);

            if (!teleported) return;

            NextAllowedTeleportTime[clientId] = Time.time + perPlayerCooldownSeconds;
            rm.ServerNotifyTeleporterEffect(player, transform.position);
        }
    }
}
