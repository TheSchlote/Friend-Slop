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
            RequestEnterRpc();
        }

        // Called by the bootstrapper when the interior scene fully unloads (last player
        // exited). Forces a fresh random layout on the next entry.
        public void ResetSeed()
        {
            if (!IsServer) return;
            _seed.Value = -1;
        }

        [Rpc(SendTo.Server)]
        private void RequestEnterRpc(RpcParams rpcParams = default)
        {
            if (definition == null) return;

            var requestingClientId = rpcParams.Receive.SenderClientId;

            if (_seed.Value < 0)
                _seed.Value = UnityEngine.Random.Range(1, int.MaxValue);

            InteriorSessionData.Seed              = _seed.Value;
            InteriorSessionData.Definition        = definition;
            // Return position: 1 m above ground, 5 m in front of the building origin
            // (clear of the 8 m shell's +Z face). transform.forward is the building's
            // surface-tangent forward direction.
            InteriorSessionData.ReturnPosition    = transform.TransformPoint(new Vector3(0f, 1f, 5f));
            InteriorSessionData.ReturnRotation    = transform.rotation;
            InteriorSessionData.RequestingClientId = requestingClientId;
            InteriorSessionData.ScenePath         = interiorScenePath;

            var service = Object.FindFirstObjectByType<NetworkSceneTransitionService>(FindObjectsInactive.Exclude);
            if (service == null) return;

            var normalized = GameScenePathUtility.NormalizePath(interiorScenePath);
            bool wasStarted = service.WasServerSceneLoadStarted(normalized);
            bool sceneLoaded = IsSceneLoaded(normalized);
            if (wasStarted || sceneLoaded)
            {
                var bootstrapper = Object.FindFirstObjectByType<InteriorSceneBootstrapper>(FindObjectsInactive.Include);
                Debug.Log($"[Interior] Re-entry — wasStarted={wasStarted} sceneLoaded={sceneLoaded} bootstrapper={(bootstrapper != null ? "found" : "MISSING")}");
                if (bootstrapper != null)
                {
                    bootstrapper.TeleportPlayerIn(requestingClientId);
                }
                else
                {
                    // Scene was unloaded but the service tracker is stale — load fresh.
                    Debug.Log("[Interior] Stale tracker — forcing fresh scene load.");
                    service.ServerLoadScenePath(interiorScenePath, LoadSceneMode.Additive);
                }
            }
            else
            {
                Debug.Log($"[Interior] First entry — loading scene {interiorScenePath}");
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
