using System;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace FriendSlop.Networking
{
    public partial class NetworkSessionManager : MonoBehaviour
    {
        // Raised when the local client/host session has ended (disconnect, shutdown,
        // transport failure). UI subscribes to release the gameplay cursor lock so the
        // menu is interactable again. Kept as an event so this class never reaches
        // upward into FriendSlop.UI.
        public static event Action SessionEnded;

        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private ushort localPort = 7777;

        private Lobby currentLobby;
        private float nextLobbyHeartbeat;
        private NetworkManager subscribedNetworkManager;
        private bool localShutdownRequested;
        private float pendingConnectionDeadline;
        private bool sessionOperationInProgress;
        private bool sessionOperationCancelled;
        private int sessionOperationId;

        public const float ConnectionTimeoutSeconds = 25f;
        private const float ServiceRequestTimeoutSeconds = 20f;

        public string LastJoinCode { get; private set; } = string.Empty;
        public string LastRelayRegion { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Not connected.";
        public bool IsSessionOperationInProgress => sessionOperationInProgress || IsPendingClientConnection();
        public bool CanCancelSessionOperation => IsSessionOperationInProgress;
        public float PendingConnectionSecondsRemaining => pendingConnectionDeadline > 0f
            ? Mathf.Max(0f, pendingConnectionDeadline - Time.realtimeSinceStartup)
            : 0f;

        public bool StartLocalHost()
        {
            if (IsSessionOperationInProgress)
            {
                Status = "Already working on a connection. Cancel it before starting another.";
                return false;
            }

            localShutdownRequested = false;
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Status = "Network Manager missing.";
                return false;
            }

            if (networkManager.IsListening)
            {
                Status = "A session is already running.";
                return false;
            }

            if (!TryGetTransport(networkManager, out var transport))
            {
                return false;
            }

            transport.SetConnectionData("127.0.0.1", localPort, "0.0.0.0");
            if (!networkManager.StartHost())
            {
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = $"Failed to start local host on port {localPort}.";
                return false;
            }

            LastJoinCode = "LAN: " + GetLocalAddressHint();
            LastRelayRegion = "LAN";
            Status = $"Local host running. Join by LAN IP on port {localPort}.";
            return true;
        }

        public bool StartLocalClient(string address)
        {
            if (IsSessionOperationInProgress)
            {
                Status = "Already working on a connection. Cancel it before starting another.";
                return false;
            }

            localShutdownRequested = false;
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Status = "Network Manager missing.";
                return false;
            }

            if (networkManager.IsListening)
            {
                Status = "A session is already running.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                address = "127.0.0.1";
            }

            if (!TryGetTransport(networkManager, out var transport))
            {
                return false;
            }

            transport.SetConnectionData(address, localPort);
            if (!networkManager.StartClient())
            {
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = $"Failed to start a client for {address}:{localPort}.";
                return false;
            }

            LastJoinCode = string.Empty;
            LastRelayRegion = "LAN";
            Status = $"Joining {address}:{localPort}...";
            BeginConnectionAttempt();
            return true;
        }

        public async void Shutdown()
        {
            localShutdownRequested = true;
            CancelPendingSessionOperation();
            await LeaveLobbyAsync();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            ResetLocalSessionState("Not connected.", clearJoinCode: true);
        }

        public void CancelSessionOperation()
        {
            localShutdownRequested = true;
            CancelPendingSessionOperation();
            _ = LeaveLobbyAsync();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            ResetLocalSessionState("Connection cancelled.", clearJoinCode: true);
        }
    }
}
