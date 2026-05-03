using System;

namespace FriendSlop.Round
{
    public static class RoundManagerRegistry
    {
        public static event Action<RoundManager, RoundManager> CurrentChanged;

        public static RoundManager Current { get; private set; }

        public static bool HasCurrent => Current != null;

        public static RoundPhase CurrentPhaseOrDefault(RoundPhase fallback = RoundPhase.Lobby)
        {
            return Current != null ? Current.Phase.Value : fallback;
        }

        public static bool IsCurrentPhase(RoundPhase phase)
        {
            return Current != null && Current.Phase.Value == phase;
        }

        internal static void Register(RoundManager roundManager)
        {
            if (roundManager == null || Current == roundManager)
                return;

            var previous = Current;
            Current = roundManager;
            CurrentChanged?.Invoke(previous, Current);
        }

        internal static void Unregister(RoundManager roundManager)
        {
            if (roundManager == null || Current != roundManager)
                return;

            var previous = Current;
            Current = null;
            CurrentChanged?.Invoke(previous, null);
        }
    }
}
