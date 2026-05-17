using FriendSlop.Interaction;
using FriendSlop.Player;
using FriendSlop.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Interiors.Blueprints
{
    // Door into a blueprint-driven interior. Same flow as InteriorEntrance — server
    // loads the interior scene additively and the bootstrapper materialises the
    // layout — except the bootstrapper reads the layout from this entrance's
    // blueprint instead of running the procedural generator.
    //
    // If `useEditorCurrent` is true, picks up the BlueprintEditor's currently-edited
    // blueprint at the moment of entry (so designers can iterate live: edit a
    // blueprint, walk through the door, see the result, walk out, edit more).
    [RequireComponent(typeof(Collider))]
    public class BlueprintEntrance : NetworkBehaviour, IFriendSlopInteractable
    {
        [Tooltip("BuildingDefinition that supplies cell size, theme colour, etc. " +
                 "The blueprint only overrides the layout — the def is still used " +
                 "for everything else.")]
        [SerializeField] private BuildingDefinition definition;
        [SerializeField] private BlueprintAsset blueprint;
        [SerializeField] private string interiorScenePath = "Assets/Scenes/Building_Interior.unity";

        public bool CanInteract(NetworkFirstPersonController player) => true;
        public string GetPrompt(NetworkFirstPersonController player) => "E enter blueprint preview";

        public void Interact(NetworkFirstPersonController player)
        {
            RequestEnterRpc(player.OwnerClientId);
        }

        [Rpc(SendTo.Server)]
        private void RequestEnterRpc(ulong requestingClientId)
        {
            // Block-blueprint path takes priority — if the BuildingDefinition
            // has a BlockBlueprintAsset wired, we use it and ignore the room
            // BlueprintAsset. Falls back to the room path when no block bp.
            var blockBp = definition != null ? definition.BlockBlueprint : null;
            BlueprintAsset resolvedBlueprint = null;
            if (blockBp == null)
            {
                resolvedBlueprint = blueprint;
                if (definition == null || resolvedBlueprint == null)
                {
                    Debug.LogWarning("[BlueprintEntrance] Missing definition or blueprint; aborting entry.");
                    return;
                }
            }
            else if (definition == null)
            {
                Debug.LogWarning("[BlueprintEntrance] Missing definition; aborting entry.");
                return;
            }

            InteriorSessionData.Seed              = 0;
            InteriorSessionData.Definition        = definition;
            InteriorSessionData.Blueprint         = resolvedBlueprint;
            InteriorSessionData.BlockBlueprint    = blockBp;
            InteriorSessionData.ReturnPosition    = transform.TransformPoint(new Vector3(0f, 1f, 5f));
            InteriorSessionData.ReturnRotation    = transform.rotation;
            InteriorSessionData.RequestingClientId = requestingClientId;
            InteriorSessionData.ScenePath         = interiorScenePath;

            var service = Object.FindFirstObjectByType<NetworkSceneTransitionService>(FindObjectsInactive.Exclude);
            if (service == null) return;

            var normalized = GameScenePathUtility.NormalizePath(interiorScenePath);
            bool wasStarted  = service.WasServerSceneLoadStarted(normalized);
            bool sceneLoaded = IsSceneLoaded(normalized);
            if (wasStarted || sceneLoaded)
            {
                var bootstrapper = Object.FindFirstObjectByType<InteriorSceneBootstrapper>(FindObjectsInactive.Include);
                if (bootstrapper != null) bootstrapper.TeleportPlayerIn(requestingClientId);
                else service.ServerLoadScenePath(interiorScenePath, LoadSceneMode.Additive);
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
