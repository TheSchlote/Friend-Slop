using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FriendSlop.Networking
{
    public partial class NetworkSessionManager
    {
        private bool TryGetTransport(NetworkManager networkManager, out UnityTransport transport)
        {
            transport = networkManager != null ? networkManager.GetComponent<UnityTransport>() : null;
            if (transport != null)
            {
                return true;
            }

            Status = "Unity Transport is missing on the Network Manager.";
            return false;
        }

        private static string GetLocalAddressHint()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var address in host.AddressList)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return address.ToString();
                    }
                }
            }
            catch
            {
                // Best-effort UI hint only.
            }

            return "127.0.0.1";
        }

        private void TrySubscribeToNetworkManager()
        {
            var networkManager = NetworkManager.Singleton;
            if (ReferenceEquals(subscribedNetworkManager, networkManager))
            {
                return;
            }

            UnsubscribeFromNetworkManager();
            subscribedNetworkManager = networkManager;
            if (subscribedNetworkManager == null)
            {
                return;
            }

            subscribedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            subscribedNetworkManager.OnClientStopped += HandleClientStopped;
            subscribedNetworkManager.OnServerStopped += HandleServerStopped;
            subscribedNetworkManager.OnTransportFailure += HandleTransportFailure;
        }

        private void UnsubscribeFromNetworkManager()
        {
            if (subscribedNetworkManager == null)
            {
                return;
            }

            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            subscribedNetworkManager.OnClientStopped -= HandleClientStopped;
            subscribedNetworkManager.OnServerStopped -= HandleServerStopped;
            subscribedNetworkManager.OnTransportFailure -= HandleTransportFailure;
            subscribedNetworkManager = null;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (subscribedNetworkManager == null ||
                clientId != subscribedNetworkManager.LocalClientId ||
                subscribedNetworkManager.IsServer)
            {
                return;
            }

            pendingConnectionDeadline = 0f;
            sessionOperationInProgress = false;
            Status = LastRelayRegion == "LAN"
                ? "Connected to LAN host."
                : "Connected to host.";
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (subscribedNetworkManager == null ||
                localShutdownRequested ||
                subscribedNetworkManager.IsServer ||
                clientId != subscribedNetworkManager.LocalClientId)
            {
                return;
            }

            Status = BuildDisconnectStatus(subscribedNetworkManager);
            sessionOperationInProgress = false;
            SessionEnded?.Invoke();
        }

        private void HandleClientStopped(bool wasHost)
        {
            if (localShutdownRequested || wasHost)
            {
                return;
            }

            ResetLocalSessionState(BuildDisconnectStatus(subscribedNetworkManager), clearJoinCode: false);
        }

        private void HandleServerStopped(bool wasHost)
        {
            if (localShutdownRequested)
            {
                return;
            }

            _ = LeaveLobbyAsync();
            ResetLocalSessionState(wasHost ? "Host session ended." : "Server stopped.", clearJoinCode: false);
        }

        private void HandleTransportFailure()
        {
            if (localShutdownRequested)
            {
                return;
            }

            localShutdownRequested = true;
            _ = LeaveLobbyAsync();

            if (subscribedNetworkManager != null && subscribedNetworkManager.IsListening)
            {
                subscribedNetworkManager.Shutdown();
            }

            ResetLocalSessionState(BuildTransportFailureStatus(), clearJoinCode: false);
        }

        private void ResetLocalSessionState(string status, bool clearJoinCode)
        {
            currentLobby = null;
            nextLobbyHeartbeat = 0f;
            pendingConnectionDeadline = 0f;
            sessionOperationInProgress = false;
            LastRelayRegion = string.Empty;
            if (clearJoinCode)
            {
                LastJoinCode = string.Empty;
            }

            Status = string.IsNullOrWhiteSpace(status) ? "Not connected." : status;
            SessionEnded?.Invoke();
        }

        private string BuildDisconnectStatus(NetworkManager networkManager)
        {
            if (networkManager != null && !string.IsNullOrWhiteSpace(networkManager.DisconnectReason))
            {
                return networkManager.DisconnectReason;
            }

            return LastRelayRegion == "LAN"
                ? "Disconnected from LAN host."
                : "Disconnected from host.";
        }

        private string BuildTransportFailureStatus()
        {
            return LastRelayRegion == "LAN"
                ? "Network transport failed. LAN session closed."
                : "Network transport failed. Session closed.";
        }

        private string BuildConnectionTimeoutStatus()
        {
            return LastRelayRegion == "LAN"
                ? "Timed out connecting to the LAN host."
                : "Timed out connecting to the host.";
        }

        private void BeginConnectionAttempt()
        {
            pendingConnectionDeadline = Time.realtimeSinceStartup + ConnectionTimeoutSeconds;
        }

        private void CheckPendingConnectionTimeout()
        {
            if (pendingConnectionDeadline <= 0f || subscribedNetworkManager == null)
            {
                return;
            }

            if (!subscribedNetworkManager.IsListening ||
                subscribedNetworkManager.IsServer ||
                subscribedNetworkManager.IsConnectedClient)
            {
                pendingConnectionDeadline = 0f;
                return;
            }

            if (Time.realtimeSinceStartup < pendingConnectionDeadline)
            {
                return;
            }

            localShutdownRequested = true;
            if (subscribedNetworkManager.IsListening)
            {
                subscribedNetworkManager.Shutdown();
            }

            ResetLocalSessionState(BuildConnectionTimeoutStatus(), clearJoinCode: false);
        }

        private bool TryBeginSessionOperation(string startingStatus, out int operationId)
        {
            operationId = 0;
            var networkManager = NetworkManager.Singleton;
            if (IsSessionOperationInProgress)
            {
                Status = "Already working on a connection. Cancel it before starting another.";
                return false;
            }

            if (networkManager != null && networkManager.IsListening)
            {
                Status = "A session is already running.";
                return false;
            }

            localShutdownRequested = false;
            sessionOperationCancelled = false;
            sessionOperationInProgress = true;
            operationId = ++sessionOperationId;
            Status = startingStatus;
            return true;
        }

        private void CancelPendingSessionOperation()
        {
            sessionOperationCancelled = true;
            sessionOperationInProgress = false;
            pendingConnectionDeadline = 0f;
            sessionOperationId++;
        }

        private bool IsSessionOperationCancelled(int operationId)
        {
            return sessionOperationCancelled || operationId != sessionOperationId;
        }

        private void CompleteSessionOperation(int operationId)
        {
            if (operationId == sessionOperationId)
            {
                sessionOperationInProgress = false;
            }
        }

        private bool IsPendingClientConnection()
        {
            var networkManager = subscribedNetworkManager != null ? subscribedNetworkManager : NetworkManager.Singleton;
            if (pendingConnectionDeadline > 0f)
            {
                return true;
            }

            return networkManager != null &&
                   networkManager.IsListening &&
                   !networkManager.IsServer &&
                   !networkManager.IsConnectedClient;
        }
    }
}
