using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.SceneManagement
{
    public class NetworkSceneTransitionService : MonoBehaviour
    {
        public static NetworkSceneTransitionService Instance { get; private set; }

        [SerializeField] private GameSceneCatalog sceneCatalog;

        private readonly HashSet<string> serverLoadedScenePaths = new();

        public GameSceneCatalog Catalog => sceneCatalog;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public SceneEventProgressStatus ServerLoadScene(GameSceneDefinition scene)
        {
            if (scene == null || !scene.IsConfigured)
            {
                return SceneEventProgressStatus.InvalidSceneName;
            }

            return ServerLoadScenePath(
                scene.ScenePath,
                scene.ShouldLoadAdditively ? LoadSceneMode.Additive : LoadSceneMode.Single);
        }

        public SceneEventProgressStatus ServerLoadScenePath(string scenePath, LoadSceneMode loadMode)
        {
            scenePath = GameScenePathUtility.NormalizePath(scenePath);
            if (!GameScenePathUtility.IsUnityScenePath(scenePath))
            {
                return SceneEventProgressStatus.InvalidSceneName;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                return SceneEventProgressStatus.ServerOnlyAction;
            }

            if (!networkManager.NetworkConfig.EnableSceneManagement || networkManager.SceneManager == null)
            {
                return SceneEventProgressStatus.SceneManagementNotEnabled;
            }

            // If the scene is already loaded (typical when the planet scene was left open
            // in the editor for authoring), skip the Netcode load. Asking Netcode to load
            // an already-loaded scene either spawns a duplicate or registers a handle for
            // a scene instance Netcode didn't actually load - both produce the
            // "Failed to remove scene handles" error on unload. We deliberately do NOT add
            // the path to serverLoadedScenePaths so the unload side bails silently and
            // the editor-preloaded scene stays as ambient state.
            if (loadMode == LoadSceneMode.Additive && IsSceneAlreadyLoaded(scenePath))
            {
                return SceneEventProgressStatus.Started;
            }

            var status = networkManager.SceneManager.LoadScene(scenePath, loadMode);
            if (status == SceneEventProgressStatus.Started)
            {
                if (loadMode == LoadSceneMode.Single)
                {
                    serverLoadedScenePaths.Clear();
                }

                serverLoadedScenePaths.Add(scenePath);
            }

            return status;
        }

        private static bool IsSceneAlreadyLoaded(string normalizedScenePath)
        {
            var sceneCount = SceneManager.sceneCount;
            for (var i = 0; i < sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                if (string.Equals(GameScenePathUtility.NormalizePath(s.path), normalizedScenePath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public bool WasServerSceneLoadStarted(string scenePath)
        {
            scenePath = GameScenePathUtility.NormalizePath(scenePath);
            return serverLoadedScenePaths.Contains(scenePath);
        }

        public SceneEventProgressStatus ServerUnloadScenePath(string scenePath)
        {
            scenePath = GameScenePathUtility.NormalizePath(scenePath);
            if (!GameScenePathUtility.IsUnityScenePath(scenePath))
            {
                return SceneEventProgressStatus.InvalidSceneName;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                return SceneEventProgressStatus.ServerOnlyAction;
            }

            if (!networkManager.NetworkConfig.EnableSceneManagement || networkManager.SceneManager == null)
            {
                return SceneEventProgressStatus.SceneManagementNotEnabled;
            }

            // Netcode can only unload scenes that it loaded - it tracks scene handles by
            // its own LoadScene calls. A scene that was already open in the editor when
            // play started (typical when authoring planets) doesn't have a handle, and
            // calling UnloadScene on it produces "Failed to remove ... scene handles".
            // Skip silently in that case; the scene stays loaded as ambient state, which
            // is the right behavior in-editor and won't happen in a built game.
            if (!serverLoadedScenePaths.Contains(scenePath))
            {
                return SceneEventProgressStatus.SceneNotLoaded;
            }

            var sceneCount = SceneManager.sceneCount;
            for (var i = 0; i < sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (!loaded.isLoaded) continue;
                if (!string.Equals(GameScenePathUtility.NormalizePath(loaded.path), scenePath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = networkManager.SceneManager.UnloadScene(loaded);
                if (status == SceneEventProgressStatus.Started)
                {
                    serverLoadedScenePaths.Remove(scenePath);
                }
                return status;
            }

            // Scene was never loaded by us in the first place - drop the bookkeeping entry
            // so callers don't think it's still around.
            serverLoadedScenePaths.Remove(scenePath);
            return SceneEventProgressStatus.SceneNotLoaded;
        }
    }
}
