using System;

namespace FriendSlop.Core
{
    // Cross-layer "should the player ignore input right now?" gate.
    //
    // The UI knows when input should be blocked (text fields focused, menu pinned, loading,
    // etc.) but Gameplay code is the consumer. To keep the dependency direction
    // Gameplay -> Core (never Gameplay -> UI), the UI registers a provider here at
    // OnEnable and Gameplay reads IsBlocked. If no provider is registered, input is never
    // blocked by default, which is the safe answer for headless tests.
    public static class GameplayInputState
    {
        private static Func<bool> _provider = () => false;

        public static bool IsBlocked => _provider();

        public static void RegisterBlockProvider(Func<bool> provider)
        {
            _provider = provider ?? (() => false);
        }

        public static void ClearBlockProvider()
        {
            _provider = () => false;
        }
    }
}
