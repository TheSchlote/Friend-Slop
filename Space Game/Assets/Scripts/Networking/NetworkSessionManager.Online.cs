using System;
using UnityEngine;

namespace FriendSlop.Networking
{
    public partial class NetworkSessionManager
    {
        public async void HostOnline()
        {
            var operationId = 0;

            try
            {
                if (!TryBeginSessionOperation("Starting online host...", out operationId))
                {
                    return;
                }

                await HostRelayAsync(operationId);
            }
            catch (Exception exception) when (operationId != 0 && !IsSessionOperationCancelled(operationId))
            {
                Debug.LogWarning($"Relay host failed, falling back to local host: {exception.Message}");
                sessionOperationInProgress = false;
                if (StartLocalHost())
                {
                    Status = $"Relay unavailable. Local host started on port {localPort}.";
                }
            }
            catch (Exception exception)
            {
                if (operationId != 0 && IsSessionOperationCancelled(operationId))
                {
                    Debug.Log($"HostOnline cancelled: {exception.Message}");
                }
                else
                {
                    Debug.LogError($"Relay host setup failed: {exception}");
                    Status = "Failed to start online host.";
                    sessionOperationInProgress = false;
                }
            }
            finally
            {
                if (operationId != 0)
                {
                    CompleteSessionOperation(operationId);
                }
            }
        }

        public async void JoinOnline(string joinCodeOrAddress)
        {
            var operationId = 0;
            var target = string.Empty;

            try
            {
                target = JoinCodeUtility.NormalizeJoinTarget(joinCodeOrAddress);
                if (string.IsNullOrWhiteSpace(target))
                {
                    Status = "Enter a Relay code, or use Join LAN for local testing.";
                    return;
                }

                if (JoinCodeUtility.LooksLikeLanAddress(target))
                {
                    StartLocalClient(target);
                    return;
                }

                if (!JoinCodeUtility.IsValidRelayJoinCode(target))
                {
                    Status = $"'{target}' is not a valid Relay code. Codes are {JoinCodeUtility.MinRelayCodeLength}-{JoinCodeUtility.MaxRelayCodeLength} letters or numbers.";
                    return;
                }

                if (!TryBeginSessionOperation($"Joining Relay code {target}...", out operationId))
                {
                    return;
                }

                await JoinRelayAsync(target, operationId);
            }
            catch (Exception exception) when (operationId != 0 && !IsSessionOperationCancelled(operationId))
            {
                Debug.LogWarning($"Relay join failed for code {target}: {exception}");
                ResetLocalSessionState(JoinCodeUtility.GetFriendlyJoinFailure(exception, target), clearJoinCode: true);
            }
            catch (Exception exception)
            {
                if (operationId != 0 && IsSessionOperationCancelled(operationId))
                {
                    Debug.Log($"JoinOnline cancelled: {exception.Message}");
                }
                else
                {
                    Debug.LogError($"Relay join setup failed: {exception}");
                    LastJoinCode = string.Empty;
                    LastRelayRegion = string.Empty;
                    Status = "Failed to start Relay join. Try again.";
                    sessionOperationInProgress = false;
                }
            }
            finally
            {
                if (operationId != 0)
                {
                    CompleteSessionOperation(operationId);
                }
            }
        }
    }
}
