using FriendSlop.Player;
using FriendSlop.Round;

namespace FriendSlop.Ship
{
    // Pilot seat. Default claim/release loop, plus the "start round" hook
    // that the legacy enum-branch logic in ShipStation used to own.
    //
    // Round-start authority lives on RoundManager (see ServerStartRound) —
    // this behavior just dispatches the call from the server-side interact
    // handler when the phase is Lobby. No singletons: we reach
    // RoundManager via RoundManagerRegistry (D-014).
    public class PilotStationBehavior : ShipStationBehavior
    {
        public override string BuildPrompt(NetworkFirstPersonController player, ShipStation host)
        {
            if (host == null) return string.Empty;

            if (CanStartRound())
            {
                return $"E start round from {host.DisplayName}";
            }

            if (player != null && host.OccupantClientId.Value == player.OwnerClientId)
            {
                return $"E leave {host.DisplayName}";
            }

            if (host.IsOccupied)
            {
                return $"{host.DisplayName} in use";
            }

            return $"E claim {host.DisplayName}";
        }

        public override void HandleInteractServer(ulong senderClientId, ShipStation host)
        {
            if (host == null) return;

            if (CanStartRound())
            {
                var roundManager = RoundManagerRegistry.Current;
                if (roundManager != null && roundManager.IsServer)
                    roundManager.ServerStartRound();
                return;
            }

            if (host.OccupantClientId.Value == senderClientId)
            {
                host.OccupantClientId.Value = ShipStation.NoOccupant;
                return;
            }

            if (host.OccupantClientId.Value == ShipStation.NoOccupant)
            {
                host.OccupantClientId.Value = senderClientId;
            }
        }

        private static bool CanStartRound()
        {
            return RoundManagerRegistry.IsCurrentPhase(RoundPhase.Lobby);
        }
    }
}
