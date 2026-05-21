using FriendSlop.Player;

namespace FriendSlop.Ship
{
    // Bare-minimum behavior: claim the station, release it on second press.
    // Mirrors the non-Pilot enum branches of the original ShipStation.
    // Use this on any station prefab that doesn't have a specialised
    // behavior — it's the "no-op skin" so every station ends up with a
    // sibling component, which lets the rest of the codebase rely on the
    // ShipStationBehavior path without falling back to the legacy enum
    // branch.
    public class LegacyOccupancyBehavior : ShipStationBehavior
    {
        public override string BuildPrompt(NetworkFirstPersonController player, ShipStation host)
        {
            if (host == null) return string.Empty;

            if (player != null && host.OccupantClientId.Value == player.OwnerClientId)
            {
                return $"E leave {host.DisplayName}";
            }

            if (host.IsOccupied)
            {
                return $"{host.DisplayName} in use";
            }

            return $"E use {host.DisplayName}";
        }

        public override void HandleInteractServer(ulong senderClientId, ShipStation host)
        {
            if (host == null) return;

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
    }
}
