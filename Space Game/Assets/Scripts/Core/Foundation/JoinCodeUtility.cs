using System;
using Unity.Services.Core;
using Unity.Services.Relay;

namespace FriendSlop.Networking
{
    public static class JoinCodeUtility
    {
        public const int MinRelayCodeLength = 6;
        public const int MaxRelayCodeLength = 12;

        private static readonly char[] CodeSeparators = { '.', ':', '\\', '/' };

        public static string NormalizeJoinTarget(string value)
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

        public static bool LooksLikeLanAddress(string value)
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

        public static string NormalizeJoinCode(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(joinCode.Length);
            foreach (var character in joinCode.Trim())
            {
                if (char.IsWhiteSpace(character) || character == '-')
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(character));
            }

            return builder.ToString();
        }

        public static bool IsValidRelayJoinCode(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode) ||
                joinCode.Length < MinRelayCodeLength ||
                joinCode.Length > MaxRelayCodeLength)
            {
                return false;
            }

            for (var i = 0; i < joinCode.Length; i++)
            {
                if (!char.IsLetterOrDigit(joinCode[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static string GetFriendlyJoinFailure(Exception exception, string joinCode)
        {
            if (exception is TimeoutException)
            {
                return "Timed out contacting Relay. Check your internet connection and try again.";
            }

            if (exception is RelayServiceException relayException)
            {
                switch (relayException.Reason)
                {
                    case RelayExceptionReason.JoinCodeNotFound:
                    case RelayExceptionReason.EntityNotFound:
                        return $"No open game found for code {joinCode}. Check the code and try again.";
                    case RelayExceptionReason.InvalidRequest:
                    case RelayExceptionReason.InvalidArgument:
                        return $"'{joinCode}' is not a valid Relay code. Check the host's code and try again.";
                    case RelayExceptionReason.Unauthorized:
                    case RelayExceptionReason.Forbidden:
                    case RelayExceptionReason.InactiveProject:
                        return "Online services are not available for this build yet. Try LAN or host a new online game.";
                    case RelayExceptionReason.RateLimited:
                        return "Relay is getting too many requests. Wait a moment and try again.";
                    case RelayExceptionReason.NetworkError:
                    case RelayExceptionReason.RequestTimeOut:
                    case RelayExceptionReason.ServiceUnavailable:
                    case RelayExceptionReason.GatewayTimeout:
                        return "Could not reach Relay. Check your internet connection and try again.";
                }
            }

            if (exception is RequestFailedException requestFailedException)
            {
                switch (requestFailedException.ErrorCode)
                {
                    case (int)RelayExceptionReason.JoinCodeNotFound:
                    case (int)RelayExceptionReason.EntityNotFound:
                        return $"No open game found for code {joinCode}. Check the code and try again.";
                    case (int)RelayExceptionReason.InvalidRequest:
                    case (int)RelayExceptionReason.InvalidArgument:
                        return $"'{joinCode}' is not a valid Relay code. Check the host's code and try again.";
                    case (int)RelayExceptionReason.NetworkError:
                    case (int)RelayExceptionReason.RequestTimeOut:
                    case (int)RelayExceptionReason.ServiceUnavailable:
                    case (int)RelayExceptionReason.GatewayTimeout:
                        return "Could not reach Relay. Check your internet connection and try again.";
                }
            }

            return $"Could not join code {joinCode}. Check the code or ask the host for a fresh one.";
        }
    }
}
