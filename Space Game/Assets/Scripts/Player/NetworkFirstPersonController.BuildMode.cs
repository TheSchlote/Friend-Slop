using UnityEngine;

namespace FriendSlop.Player
{
    // Owner-side time-scale escape hatch. The 2D blueprint editor pauses the world
    // with Time.timeScale = 0 to freeze monsters/AI/loot. The 3D blueprint editor
    // wants the same freeze, but the player must still be able to walk and look —
    // so while UseUnscaledOwnerTime is true, every Time.deltaTime call in the FPS
    // controller routes through OwnerDeltaTime, which falls back to unscaledDeltaTime.
    //
    // Only the FPS controller itself flips its own clock — everything else (monsters,
    // doors, loot physics, NPCs) keeps using Time.deltaTime and naturally freezes
    // when timeScale is zero.
    public partial class NetworkFirstPersonController
    {
        public static bool UseUnscaledOwnerTime;

        private static float OwnerDeltaTime =>
            UseUnscaledOwnerTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }
}
