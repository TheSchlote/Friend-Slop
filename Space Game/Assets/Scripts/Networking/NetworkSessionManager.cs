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

        public string LastJoinCode { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Not connected.";

        private static readonly char[] CodeSeparators = { '.', ':', '\\', '/' };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private async void Update()
        {
            if (currentLobby == null || Time.realtimeSinceStartup < nextLobbyHeartbeat)
            {
                return;
            }

            nextLobbyHeartbeat = Time.realtimeSinceStartup + 15f;
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lobby heartbeat failed: {exception.Message}");
            }
        }

        public async void HostOnline()
        {
            try
            {
                await HostRelayAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Relay host failed, falling back to local host: {exception.Message}");
                StartLocalHost();
                Status = $"Relay unavailable. Local host started on port {localPort}.";
            }
        }

        public async void JoinOnline(string joinCodeOrAddress)
        {
            var target = NormalizeJoinTarget(joinCodeOrAddress);
            if (string.IsNullOrWhiteSpace(target))
            {
                StartLocalClient("127.0.0.1");
                return;
            }

            if (LooksLikeLanAddress(target))
            {
                StartLocalClient(target);
                return;
            }

            try
            {
                await JoinRelayAsync(target);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Relay join failed: {exception.Message}");
                Status = $"Join code failed: {exception.Message}";
            }
        }

        public void StartLocalHost()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.IsListening)
            {
                return;
            }

            var transport = networkManager.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", localPort, "0.0.0.0");
            LastJoinCode = "LAN: " + GetLocalAddressHint();
            Status = $"Local host running. Join by LAN IP on port {localPort}.";
            networkManager.StartHost();
        }

        public void StartLocalClient(string address)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.IsListening)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                address = "127.0.0.1";
            }

            var transport = networkManager.GetComponent<UnityTransport>();
            transport.SetConnectionData(address, localPort);
            LastJoinCode = string.Empty;
            Status = $"Joining {address}:{localPort}...";
            networkManager.StartClient();
        }

        public async void Shutdown()
        {
            await LeaveLobbyAsync();

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            LastJoinCode = string.Empty;
            Status = "Not connected.";
        }

        private async Task HostRelayAsync()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.IsListening)
            {
                return;
            }

            Status = "Signing in to Unity services...";
            await EnsureSignedInAsync();

            Status = "Creating Relay allocation...";
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            LastJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var transport = networkManager.GetComponent<UnityTransport>();
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

            Status = $"Relay host running. Code: {LastJoinCode}";
            networkManager.StartHost();
        }

        private async Task JoinRelayAsync(string joinCode)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.IsListening)
            {
                return;
            }

            joinCode = NormalizeJoinCode(joinCode);
            Status = "Signing in to Unity services...";
            await EnsureSignedInAsync();

            Status = $"Joining Relay code {joinCode}...";
            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = networkManager.GetComponent<UnityTransport>();
            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));

            LastJoinCode = joinCode;
            Status = $"Joining Relay code {joinCode}...";
            networkManager.StartClient();
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

        private static bool LooksLikeLanAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return trimmed.IndexOfAny(CodeSeparators) >= 0;
        }

        private static string NormalizeJoinTarget(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("join code:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("join code:".Length).Trim();
            }
            else if (trimmed.StartsWith("code:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("code:".Length).Trim();
            }
            else if (trimmed.StartsWith("lan:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("lan:".Length).Trim();
            }

            return LooksLikeLanAddress(trimmed) ? trimmed : NormalizeJoinCode(trimmed);
        }

        private static string NormalizeJoinCode(string joinCode)
        {
            return string.IsNullOrWhiteSpace(joinCode)
                ? string.Empty
                : joinCode.Trim().ToUpperInvariant();
        }
    }
}
