using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using UnityEngine;

namespace FriendSlop.Networking
{
    public partial class NetworkSessionManager
    {
        private void Awake()
        {
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            TrySubscribeToNetworkManager();
        }

        private void OnEnable()
        {
            TrySubscribeToNetworkManager();
        }

        private async void Update()
        {
            TrySubscribeToNetworkManager();
            CheckPendingConnectionTimeout();

            if (currentLobby == null || Time.realtimeSinceStartup < nextLobbyHeartbeat)
            {
                return;
            }

            nextLobbyHeartbeat = Time.realtimeSinceStartup + 15f;
            try
            {
                await WithServiceTimeout(LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id), "Lobby heartbeat");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lobby heartbeat failed: {exception.Message}");
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromNetworkManager();
        }

        private void OnApplicationQuit()
        {
            _ = LeaveLobbyAsync();
        }

        private void OnDestroy()
        {
            TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
            UnsubscribeFromNetworkManager();
        }

        private void HandleUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Debug.LogError($"Unobserved session task exception: {args.Exception}");
            args.SetObserved();
        }
    }
}
