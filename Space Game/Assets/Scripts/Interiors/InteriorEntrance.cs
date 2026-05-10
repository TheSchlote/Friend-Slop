using FriendSlop.Interaction;
using FriendSlop.Player;
using FriendSlop.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Interiors
{
    // Place on the building exterior door. Player looks at it and presses E.
    // Server loads the interior scene additively; InteriorSceneBootstrapper handles generation.
    [RequireComponent(typeof(Collider))]
    public class InteriorEntrance : NetworkBehaviour, IFriendSlopInteractable
    {
        [SerializeField] private BuildingDefinition definition;
        [SerializeField] private string interiorScenePath = "Assets/Scenes/Building_Interior.unity";

        private readonly NetworkVariable<int> _seed =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool CanInteract(NetworkFirstPersonController player) => true;
        public string GetPrompt(NetworkFirstPersonController player) => "E enter building";

        public void Interact(NetworkFirstPersonController player)
        {
            RequestEnterRpc(player.OwnerClientId);
        }

        [Rpc(SendTo.Server)]
        private void RequestEnterRpc(ulong requestingClientId)
        {
            if (definition == null) return;

            if (_seed.Value < 0)
                _seed.Value = (Mathf.RoundToInt(transform.position.x) * 31
                             + Mathf.RoundToInt(transform.position.z)) & int.MaxValue;

            InteriorSessionData.Seed              = _seed.Value;
            InteriorSessionData.Definition        = definition;
            InteriorSessionData.ReturnPosition    = transform.position + transform.up * 1.5f;
            InteriorSessionData.ReturnRotation    = transform.rotation;
            InteriorSessionData.RequestingClientId = requestingClientId;
            InteriorSessionData.ScenePath         = interiorScenePath;

            var service = Object.FindFirstObjectByType<NetworkSceneTransitionService>(FindObjectsInactive.Exclude);
            if (service == null) return;

            var normalized = GameScenePathUtility.NormalizePath(interiorScenePath);
            if (service.WasServerSceneLoadStarted(normalized) || IsSceneLoaded(normalized))
            {
                // Interior already loaded — just teleport this player in.
                var bootstrapper = Object.FindFirstObjectByType<InteriorSceneBootstrapper>(FindObjectsInactive.Include);
                bootstrapper?.TeleportPlayerIn(requestingClientId);
            }
            else
            {
                service.ServerLoadScenePath(interiorScenePath, LoadSceneMode.Additive);
            }
        }

        private static bool IsSceneLoaded(string normalizedPath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && string.Equals(
                        GameScenePathUtility.NormalizePath(s.path),
                        normalizedPath,
                        System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
