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
    public class NetworkSessionManager : MonoBehaviour
    {
        public static NetworkSessionManager Instance { get; private set; }

        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private ushort localPort = 7777;

        private Lobby currentLobby;
        private float nextLobbyHeartbeat;
        private bool heartbeatInFlight;

        public string LastJoinCode { get; private set; } = string.Empty;
        public string LastRelayRegion { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Not connected.";
        public bool IsBusy { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            if (currentLobby == null || heartbeatInFlight || Time.realtimeSinceStartup < nextLobbyHeartbeat)
            {
                return;
            }

            nextLobbyHeartbeat = Time.realtimeSinceStartup + 15f;
            _ = SendLobbyHeartbeatAsync(currentLobby.Id);
        }

        private async Task SendLobbyHeartbeatAsync(string lobbyId)
        {
            heartbeatInFlight = true;
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lobby heartbeat failed: {exception.Message}");
            }
            finally
            {
                heartbeatInFlight = false;
            }
        }

        public void HostOnline()
        {
            _ = RunSessionOperationAsync(HostOnlineAsync);
        }

        public void JoinOnline(string joinCodeOrAddress)
        {
            _ = RunSessionOperationAsync(() => JoinOnlineAsync(joinCodeOrAddress));
        }

        public bool StartLocalHost()
        {
            if (IsBusy)
            {
                Status = "Please wait for the current session request to finish.";
                return false;
            }

            return StartLocalHostInternal();
        }

        public bool StartLocalClient(string address)
        {
            if (IsBusy)
            {
                Status = "Please wait for the current session request to finish.";
                return false;
            }

            return StartLocalClientInternal(address);
        }

        public void Shutdown()
        {
            _ = RunSessionOperationAsync(ShutdownAsync);
        }

        private async Task RunSessionOperationAsync(Func<Task> operation)
        {
            if (IsBusy)
            {
                Status = "Please wait for the current session request to finish.";
                return;
            }

            IsBusy = true;
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Unexpected session operation failure: {exception}");
                Status = "Session request failed unexpectedly.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task HostOnlineAsync()
        {
            try
            {
                await HostRelayAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Relay host failed, falling back to local host: {exception.Message}");
                if (StartLocalHostInternal())
                {
                    Status = $"Relay unavailable. Local host started on port {localPort}.";
                }
            }
        }

        private async Task JoinOnlineAsync(string joinCodeOrAddress)
        {
            var target = JoinCodeUtility.NormalizeJoinTarget(joinCodeOrAddress);
            if (string.IsNullOrWhiteSpace(target))
            {
                Status = "Enter a Relay code, or use Join LAN for local testing.";
                return;
            }

            if (JoinCodeUtility.LooksLikeLanAddress(target))
            {
                StartLocalClientInternal(target);
                return;
            }

            if (!JoinCodeUtility.IsValidRelayJoinCode(target))
            {
                Status = $"'{target}' is not a valid Relay code. Codes are {JoinCodeUtility.MinRelayCodeLength}-{JoinCodeUtility.MaxRelayCodeLength} letters or numbers.";
                return;
            }

            try
            {
                await JoinRelayAsync(target);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Relay join failed for code {target}: {exception}");
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = JoinCodeUtility.GetFriendlyJoinFailure(exception, target);
            }
        }

        private bool StartLocalHostInternal()
        {
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

        private bool StartLocalClientInternal(string address)
        {
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
            return true;
        }

        private async Task ShutdownAsync()
        {
            await LeaveLobbyAsync();

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            LastJoinCode = string.Empty;
            LastRelayRegion = string.Empty;
            Status = "Not connected.";
        }

        private async Task HostRelayAsync()
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
            await EnsureSignedInAsync();

            Status = "Creating Relay allocation...";
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            LastJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            LastRelayRegion = allocation.Region;

            if (!TryGetTransport(networkManager, out var transport))
            {
                throw new InvalidOperationException(Status);
            }

            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            try
            {
                currentLobby = await LobbyService.Instance.CreateLobbyAsync(
                    "Friend Slop",
                    maxPlayers,
                    new CreateLobbyOptions
                    {
                        IsPrivate = false,
                        Data = new Dictionary<string, DataObject>
                        {
                            { "RelayCode", new DataObject(DataObject.VisibilityOptions.Public, LastJoinCode) }
                        }
                    });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lobby creation failed; continuing with Relay code only: {exception.Message}");
            }

            if (!networkManager.StartHost())
            {
                await LeaveLobbyAsync();
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = "Failed to start Relay host.";
                throw new InvalidOperationException("Netcode failed to start Relay host.");
            }

            Status = $"Relay host running. Code: {LastJoinCode}";
        }

        private async Task JoinRelayAsync(string joinCode)
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
            await EnsureSignedInAsync();

            Status = $"Joining Relay code {joinCode}...";
            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            LastRelayRegion = joinAllocation.Region;

            if (!TryGetTransport(networkManager, out var transport))
            {
                throw new InvalidOperationException(Status);
            }

            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));
            if (!networkManager.StartClient())
            {
                LastJoinCode = string.Empty;
                LastRelayRegion = string.Empty;
                Status = $"Failed to join Relay code {joinCode}.";
                throw new InvalidOperationException("Netcode failed to start Relay client.");
            }

            LastJoinCode = joinCode;
            Status = $"Joining Relay code {joinCode}...";
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
                    await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
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

        private void OnApplicationQuit()
        {
            _ = LeaveLobbyAsync();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
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
    }
}
