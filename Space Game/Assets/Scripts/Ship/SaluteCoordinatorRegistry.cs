using System;

namespace FriendSlop.Ship
{
    // Narrow registry for the in-scene SaluteCoordinator. Mirrors
    // DayNightCycleRegistry / RoundManagerRegistry — see D-014 (no singletons:
    // owners self-register on spawn, unregister on despawn; callers ask the
    // registry for the current instance, can't reach for a static `.Instance`).
    public static class SaluteCoordinatorRegistry
    {
        public static event Action<SaluteCoordinator, SaluteCoordinator> CurrentChanged;

        public static SaluteCoordinator Current { get; private set; }

        internal static void Register(SaluteCoordinator coordinator)
        {
            if (coordinator == null || Current == coordinator)
                return;

            var previous = Current;
            Current = coordinator;
            CurrentChanged?.Invoke(previous, Current);
        }

        internal static void Unregister(SaluteCoordinator coordinator)
        {
            if (coordinator == null || Current != coordinator)
                return;

            var previous = Current;
            Current = null;
            CurrentChanged?.Invoke(previous, null);
        }
    }
}
