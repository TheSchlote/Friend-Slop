using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
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

        private async Task HostRelayAsync(int operationId)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                throw new InvalidOperationException("Network Manager missing.");
            }

            if (networkManager.IsListening)
            {
                throw new InvalidOperationException("A session is already running.");
            }

            Status = "Signing in to Unity services...";
            await WithServiceTimeout(EnsureSignedInAsync(), "Unity Services sign-in");
            if (IsSessionOperationCancelled(operationId)) return;

            Status = "Creating Relay allocation...";
            var allocation = await WithServiceTimeout(RelayService.Instance.CreateAllocationAsync(maxPlayers - 1), "Relay allocation");
            if (IsSessionOperationCancelled(operationId)) return;
            LastJoinCode = await WithServiceTimeout(RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId), "Relay join-code request");
            if (IsSessionOperationCancelled(operationId)) return;
            LastRelayRegion = allocation.Region;

            if (!TryGetTransport(networkManager, out var transport))
            {
                throw new InvalidOperationException(Status);
            }

            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            try
            {
                currentLobby = await WithServiceTimeout(LobbyService.Instance.CreateLobbyAsync(
                    "Friend Slop",
                    maxPlayers,
                    new CreateLobbyOptions
                    {
                        IsPrivate = false,
                        Data = new Dictionary<string, DataObject>
                        {
                            { "RelayCode", new DataObject(DataObject.VisibilityOptions.Public, LastJoinCode) }
                        }
                    }), "Lobby creation");
                if (IsSessionOperationCancelled(operationId))
                {
                    await LeaveLobbyAsync();
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lobby creation failed; continuing with Relay code only: {exception.Message}");
            }

            if (IsSessionOperationCancelled(operationId))
            {
                await LeaveLobbyAsync();
                return;
            }

            if (!networkManager.StartHost())
            {
                await LeaveLobbyAsync();
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = "Failed to start Relay host.";
                throw new InvalidOperationException("Netcode failed to start Relay host.");
            }

            Status = "Relay host running.";
        }

        private async Task JoinRelayAsync(string joinCode, int operationId)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                throw new InvalidOperationException("Network Manager missing.");
            }

            if (networkManager.IsListening)
            {
                throw new InvalidOperationException("A session is already running.");
            }

            joinCode = JoinCodeUtility.NormalizeJoinCode(joinCode);
            Status = "Signing in to Unity services...";
            await WithServiceTimeout(EnsureSignedInAsync(), "Unity Services sign-in");
            if (IsSessionOperationCancelled(operationId)) return;

            Status = $"Joining Relay code {joinCode}...";
            var joinAllocation = await WithServiceTimeout(RelayService.Instance.JoinAllocationAsync(joinCode), "Relay join");
            if (IsSessionOperationCancelled(operationId)) return;
            LastRelayRegion = joinAllocation.Region;

            if (!TryGetTransport(networkManager, out var transport))
            {
                throw new InvalidOperationException(Status);
            }

            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));
            if (IsSessionOperationCancelled(operationId)) return;
            if (!networkManager.StartClient())
            {
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = $"Failed to join Relay code {joinCode}.";
                throw new InvalidOperationException("Netcode failed to start Relay client.");
            }

            LastJoinCode = joinCode;
            Status = $"Joining Relay code {joinCode}...";
            BeginConnectionAttempt();
        }

        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private async Task LeaveLobbyAsync()
        {
            if (currentLobby == null)
            {
                return;
            }

            try
            {
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    await WithServiceTimeout(LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id), "Lobby cleanup");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lobby cleanup failed: {exception.Message}");
            }
            finally
            {
                currentLobby = null;
            }
        }

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

        private static async Task WithServiceTimeout(Task task, string operationName)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ServiceRequestTimeoutSeconds));
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"{operationName} timed out.");
            }

            await task;
        }

        private static async Task<T> WithServiceTimeout<T>(Task<T> task, string operationName)
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ServiceRequestTimeoutSeconds));
            var completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"{operationName} timed out.");
            }

            return await task;
        }
    }
}
