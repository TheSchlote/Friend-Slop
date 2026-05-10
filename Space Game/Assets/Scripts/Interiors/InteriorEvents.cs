using System;

namespace FriendSlop.Interiors
{
    public static class InteriorEvents
    {
        // true = show loading screen, false = hide it
        public static event Action<bool> LoadingStateChanged;

        internal static void SetLoading(bool visible) => LoadingStateChanged?.Invoke(visible);
    }
}
