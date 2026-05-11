using FriendSlop.Interaction;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Place on the exit door inside the interior scene.
    // Player looks at it, presses E, and is teleported back to the planet surface.
    [RequireComponent(typeof(Collider))]
    public class InteriorExitDoor : NetworkBehaviour, IFriendSlopInteractable
    {
        public bool CanInteract(NetworkFirstPersonController player) => true;
        public string GetPrompt(NetworkFirstPersonController player) => "E exit building";

        public void Interact(NetworkFirstPersonController player)
        {
            RequestExitRpc();
        }

        [Rpc(SendTo.Server)]
        private void RequestExitRpc(RpcParams rpcParams = default)
        {
            var clientId = rpcParams.Receive.SenderClientId;
            var player = FindPlayer(clientId);
            if (player == null) return;

            var bootstrapper = Object.FindFirstObjectByType<InteriorSceneBootstrapper>(FindObjectsInactive.Include);
            if (bootstrapper == null) return;

            var returnPos = bootstrapper.ReturnPosition;
            var returnRot = bootstrapper.ReturnRotation;

            player.ServerTeleport(returnPos, returnRot);
            bootstrapper.PlayerExited(clientId);
        }

        private static NetworkFirstPersonController FindPlayer(ulong clientId)
        {
            foreach (var c in Object.FindObjectsByType<NetworkFirstPersonController>(FindObjectsSortMode.None))
                if (c.OwnerClientId == clientId) return c;
            return null;
        }
    }
}
