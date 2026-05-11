using UnityEngine;

namespace FriendSlop.Core
{
    // Optional plug-in for SphereWorld. When a planet's surface isn't a perfect sphere
    // (procedurally displaced terrain, future deformable craters, etc.), the world
    // queries this provider to get the actual radial offset at a given outward
    // direction. Anchor placement (launchpad, beacons, spawn points) routes through
    // SphereWorld.GetSurfacePoint, so respecting this interface is enough to keep
    // every existing piece of authored content sitting on the displaced surface.
    public interface ISphereSurfaceHeightProvider
    {
        // unitNormal is the outward radial direction from the sphere center; the
        // returned value is added to the world's base radius before any caller-supplied
        // height offset. Implementations must be deterministic for a given direction so
        // surface queries match across network clients running off the same seed.
        float GetHeightAt(Vector3 unitNormal);
    }
}
