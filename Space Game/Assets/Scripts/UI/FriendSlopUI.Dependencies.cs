using FriendSlop.Core;
using FriendSlop.Networking;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.UI
{
    public partial class FriendSlopUI
    {
        [SerializeField] private NetworkSessionManager sessionManager;

        public static bool BlocksGameplayInput => GameplayInputState.IsBlocked;

        private NetworkSessionManager SessionManager
        {
            get
            {
                if (sessionManager != null)
                {
                    return sessionManager;
                }

                var networkManager = NetworkManager.Singleton;
                if (networkManager != null)
                {
                    sessionManager = networkManager.GetComponent<NetworkSessionManager>();
                }

                return sessionManager;
            }
        }
    }
}
