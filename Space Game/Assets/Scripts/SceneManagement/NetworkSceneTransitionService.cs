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

        public bool WasServerSceneLoadStarted(string scenePath)
        {
            scenePath = GameScenePathUtility.NormalizePath(scenePath);
            return serverLoadedScenePaths.Contains(scenePath);
        }
    }
}
