using System;
using UnityEngine;

namespace FriendSlop.Player
{
    public static class LocalPlayerRegistry
    {
        public static event Action<NetworkFirstPersonController, NetworkFirstPersonController> CurrentChanged;
        public static event Action Damaged;
        public static event Action JoinedActiveRound;

        public static NetworkFirstPersonController Current { get; private set; }
        public static Camera CurrentCamera => Current != null ? Current.PlayerCamera : null;

        internal static void Register(NetworkFirstPersonController player)
        {
            if (player == null || Current == player)
                return;

            var previous = Current;
            Current = player;
            CurrentChanged?.Invoke(previous, Current);
        }

        internal static void Unregister(NetworkFirstPersonController player)
        {
            if (player == null || Current != player)
                return;

            var previous = Current;
            Current = null;
            CurrentChanged?.Invoke(previous, null);
        }

        internal static void NotifyDamaged()
        {
            Damaged?.Invoke();
        }

        internal static void NotifyJoinedActiveRound()
        {
            JoinedActiveRound?.Invoke();
        }
    }
}
