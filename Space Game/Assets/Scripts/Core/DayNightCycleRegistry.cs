using System;

namespace FriendSlop.Core
{
    public static class DayNightCycleRegistry
    {
        public static event Action<DayNightCycle, DayNightCycle> CurrentChanged;

        public static DayNightCycle Current { get; private set; }

        internal static void Register(DayNightCycle cycle)
        {
            if (cycle == null || Current == cycle)
                return;

            var previous = Current;
            Current = cycle;
            CurrentChanged?.Invoke(previous, Current);
        }

        internal static void Unregister(DayNightCycle cycle)
        {
            if (cycle == null || Current != cycle)
                return;

            var previous = Current;
            Current = null;
            CurrentChanged?.Invoke(previous, null);
        }
    }
}
