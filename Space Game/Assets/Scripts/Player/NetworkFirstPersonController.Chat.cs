using System;
using Unity.Netcode;

namespace FriendSlop.Player
{
    // Player-owned chat RPCs. UI subscribes to ChatMessageReceived and renders messages.
    public partial class NetworkFirstPersonController
    {
        public static event Action<string, string> ChatMessageReceived;

        public const int MaxChatMessageLength = 200;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SendChatMessageServerRpc(string message, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (string.IsNullOrWhiteSpace(message)) return;

            message = message.Replace('<', ' ').Replace('>', ' ').Trim();
            if (message.Length == 0) return;
            if (message.Length > MaxChatMessageLength) message = message[..MaxChatMessageLength];

            var sender = !string.IsNullOrEmpty(_displayName) ? _displayName : DisplayName;
            sender = (sender ?? "Player").Replace('<', ' ').Replace('>', ' ');
            BroadcastChatClientRpc(sender, message);
        }

        [ClientRpc]
        private void BroadcastChatClientRpc(string sender, string message)
        {
            ChatMessageReceived?.Invoke(sender, message);
        }
    }
}
