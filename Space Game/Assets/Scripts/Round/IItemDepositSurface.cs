using System.Collections.Generic;
using FriendSlop.Loot;
using FriendSlop.Player;

namespace FriendSlop.Round
{
    // A volume in the world where a player can choose to deposit a carried item. Both
    // DepositZone (junk loot) and LaunchpadZone (ship parts + junk) implement this; the
    // PlayerInteractor walks the registry each frame to decide whether the local player
    // is standing in a depositable surface and what label to surface in the HUD prompt.
    public interface IItemDepositSurface
    {
        bool ContainsPlayer(NetworkFirstPersonController player);
        bool Accepts(NetworkLootItem item);
        // Server-side: actually performs the deposit. Caller is responsible for
        // re-validating ownership and zone presence; the surface only routes to the
        // RoundManager method that owns the corresponding gameplay state.
        void ServerSubmit(NetworkLootItem item);
        // Short, player-facing description of what depositing here does, used to build
        // the prompt (e.g. "deposit at launchpad", "drop at deposit chute").
        string DepositLabel { get; }
    }

    public static class ItemDepositSurface
    {
        private static readonly List<IItemDepositSurface> Active = new();

        public static void Register(IItemDepositSurface surface)
        {
            if (surface == null || Active.Contains(surface)) return;
            Active.Add(surface);
        }

        public static void Unregister(IItemDepositSurface surface)
        {
            Active.Remove(surface);
        }

        // Returns the highest-priority surface containing the player that accepts the
        // given item, or null if none does. Order is registration order; current callers
        // only have one of each kind in scope at a time so a single sweep is sufficient.
        public static IItemDepositSurface FindFor(NetworkFirstPersonController player, NetworkLootItem item)
        {
            if (player == null || item == null) return null;
            for (var i = 0; i < Active.Count; i++)
            {
                var surface = Active[i];
                if (surface == null) continue;
                if (!surface.ContainsPlayer(player)) continue;
                if (!surface.Accepts(item)) continue;
                return surface;
            }
            return null;
        }
    }
}
