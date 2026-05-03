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
        private static readonly Func<bool> DefaultProvider = () => false;
        private static Func<bool> _provider = DefaultProvider;
        private static object _providerOwner;

        public static bool IsBlocked => _provider();

        public static void RegisterBlockProvider(Func<bool> provider)
        {
            RegisterBlockProvider(provider?.Target, provider);
        }

        public static void RegisterBlockProvider(object owner, Func<bool> provider)
        {
            _providerOwner = owner;
            _provider = provider ?? DefaultProvider;
        }

        public static void ClearBlockProvider()
        {
            _providerOwner = null;
            _provider = DefaultProvider;
        }

        public static void ClearBlockProvider(object owner)
        {
            if (_providerOwner != owner)
            {
                return;
            }

            ClearBlockProvider();
        }
    }
}
