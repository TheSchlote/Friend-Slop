using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Ship
{
    // Abstract base for a sibling component pattern that replaces the
    // ShipStationRole enum branch as the canonical way to specialise a
    // station. The pattern (cf. D-014: no singletons): a ShipStation hosts
    // a generic occupancy NetworkVariable and an interact RPC; a sibling
    // ShipStationBehavior on the same GameObject decides what the prompt
    // says, what `AllowsInteract` means, and what the server does when the
    // interact RPC fires.
    //
    // Each concrete subclass is a sibling component on the station's
    // GameObject — there is no global registry, no singleton instance, and
    // no scriptable-object "behavior catalog". Authoring a new station type
    // is: add a new subclass + drop it on a new ShipStation prefab.
    //
    // Lifecycle: ShipStation.OnNetworkSpawn caches its sibling behavior
    // (GetComponent<ShipStationBehavior>()) and forwards the spawn/despawn
    // callbacks through OnHostNetworkSpawn / OnHostNetworkDespawn. The host
    // is passed in to every contract method so the subclass doesn't have to
    // cache it itself.
    [RequireComponent(typeof(NetworkObject))]
    public abstract class ShipStationBehavior : NetworkBehaviour
    {
        // Returns the prompt the player sees when looking at the station.
        // Must be non-null (return "" for no prompt). Called on every client
        // every frame the station has focus — keep it cheap.
        public abstract string BuildPrompt(
            NetworkFirstPersonController player,
            ShipStation host);

        // Server-only interact handler. The host has already validated that
        // (a) the sender matches the rpc params client id, and (b) the ship-
        // phase guard passes if `host.requiresShipPhase` is set. Subclasses
        // therefore don't need to repeat those checks.
        public abstract void HandleInteractServer(
            ulong senderClientId,
            ShipStation host);

        // Gates whether the prompt shows / interact can fire. The default
        // gate matches the original ShipStation behavior: "not occupied, or
        // I'm the occupant". Subclasses override when their interaction
        // semantics differ — MissionVote, for instance, never blocks anyone
        // from voting.
        public virtual bool AllowsInteract(
            NetworkFirstPersonController player,
            ShipStation host)
        {
            if (player == null || host == null) return false;
            return !host.IsOccupied
                || host.OccupantClientId.Value == player.OwnerClientId;
        }

        // Spawn / despawn hooks. Subclasses override to subscribe to
        // NetworkVariable changes, build initial state, or undo same.
        public virtual void OnHostNetworkSpawn(ShipStation host) { }
        public virtual void OnHostNetworkDespawn(ShipStation host) { }
    }
}
