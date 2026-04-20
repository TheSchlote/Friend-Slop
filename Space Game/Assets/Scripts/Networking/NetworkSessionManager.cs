using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace FriendSlop.Networking
{
    public class NetworkSessionManager : MonoBehaviour
    {
        public static NetworkSessionManager Instance { get; private set; }

        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private ushort localPort = 7777;

        private ISession currentSession;
        private bool sessionOperationInProgress;

        public string LastJoinCode { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Not connected.";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public async void HostOnline()
        {
            if (!CanStartSession())
            {
                return;
            }

            sessionOperationInProgress = true;
            try
            {
                Status = "Signing in to Unity services...";
                await EnsureSignedInAsync();

                Status = "Creating online session...";
                var options = new SessionOptions
                {
                    Name = "Friend Slop",
                    MaxPlayers = maxPlayers,
                    IsPrivate = false
                }.WithRelayNetwork();

                RegisterSession(await MultiplayerService.Instance.CreateSessionAsync(options));
                LastJoinCode = currentSession.Code;
                Status = $"Online host running. Share join code {LastJoinCode}.";
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Online host failed: {exception}");
                LastJoinCode = string.Empty;
                Status = $"Online host failed: {exception.Message}";
            }
            finally
            {
                sessionOperationInProgress = false;
            }
        }

        public async void JoinOnline(string joinCode)
        {
            if (!CanStartSession())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Status = "Enter an online join code.";
                return;
            }

            sessionOperationInProgress = true;
            try
            {
                var trimmedJoinCode = joinCode.Trim();
                Status = "Signing in to Unity services...";
                await EnsureSignedInAsync();

                Status = $"Joining online code {trimmedJoinCode}...";
                RegisterSession(await MultiplayerService.Instance.JoinSessionByCodeAsync(trimmedJoinCode));
                LastJoinCode = trimmedJoinCode;
                Status = $"Joined online session {trimmedJoinCode}.";
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Online join failed: {exception}");
                LastJoinCode = string.Empty;
                Status = $"Online join failed: {exception.Message}";
            }
            finally
            {
                sessionOperationInProgress = false;
            }
        }

        public void StartLocalHost()
        {
            if (!CanStartSession())
            {
                return;
            }

            var networkManager = NetworkManager.Singleton;
            var transport = networkManager.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", localPort, "0.0.0.0");
            LastJoinCode = GetLocalAddressHint();
            Status = $"Local host running. Join by LAN IP on port {localPort}.";
            networkManager.StartHost();
        }

        public void StartLocalClient(string address)
        {
            if (!CanStartSession())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                address = "127.0.0.1";
            }

            var trimmedAddress = address.Trim();
            var networkManager = NetworkManager.Singleton;
            var transport = networkManager.GetComponent<UnityTransport>();
            transport.SetConnectionData(trimmedAddress, localPort);
            LastJoinCode = string.Empty;
            Status = $"Joining {trimmedAddress}:{localPort}...";
            networkManager.StartClient();
        }

        public async void Shutdown()
        {
            if (sessionOperationInProgress)
            {
                return;
            }

            sessionOperationInProgress = true;
            try
            {
                if (currentSession != null)
                {
                    var leavingSession = currentSession;
                    UnregisterSession();
                    await leavingSession.LeaveAsync();
                }
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Session shutdown failed: {exception}");
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }
            }
            finally
            {
                LastJoinCode = string.Empty;
                Status = "Not connected.";
                sessionOperationInProgress = false;
            }
        }

        private bool CanStartSession()
        {
            if (sessionOperationInProgress)
            {
                return false;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Status = "No NetworkManager in scene.";
                return false;
            }

            return !networkManager.IsListening;
        }

        private void RegisterSession(ISession session)
        {
            UnregisterSession();
            currentSession = session;
            currentSession.PlayerJoined += OnSessionRosterChanged;
            currentSession.PlayerHasLeft += OnSessionRosterChanged;
            currentSession.RemovedFromSession += OnRemovedFromSession;
            currentSession.Deleted += OnSessionDeleted;
        }

        private void UnregisterSession()
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.PlayerJoined -= OnSessionRosterChanged;
            currentSession.PlayerHasLeft -= OnSessionRosterChanged;
            currentSession.RemovedFromSession -= OnRemovedFromSession;
            currentSession.Deleted -= OnSessionDeleted;
            currentSession = null;
        }

        private void OnSessionRosterChanged(string _)
        {
            if (currentSession == null)
            {
                return;
            }

            Status = currentSession.IsHost
                ? $"Online host running. Share join code {currentSession.Code}."
                : $"Joined online session {currentSession.Code}.";
        }

        private void OnRemovedFromSession()
        {
            UnregisterSession();
            LastJoinCode = string.Empty;
            Status = "Removed from online session.";
        }

        private void OnSessionDeleted()
        {
            UnregisterSession();
            LastJoinCode = string.Empty;
            Status = "Online session closed.";
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
