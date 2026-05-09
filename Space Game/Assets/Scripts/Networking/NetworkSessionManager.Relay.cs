using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
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
    public partial class NetworkSessionManager
    {
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
