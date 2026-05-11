using UnityEngine;

namespace FriendSlop.Round
{
    // Marker for an individual water disk dropped into a procedural planet's valleys.
    // The terrain generator instantiates these at runtime, sets a translucent ice-blue
    // material, and walks them as siblings under a "Water Pools" parent. No collider:
    // for now players simply walk through. Future drag/swim mechanics can read this
    // component to find pools they're submerged in.
    public class PlanetWaterPool : MonoBehaviour
    {
        public Vector3 SurfaceNormal { get; private set; }
        public float DiskRadius { get; private set; }

        public void Configure(Vector3 surfaceNormal, float diskRadius)
        {
            SurfaceNormal = surfaceNormal.normalized;
            DiskRadius = Mathf.Max(0.1f, diskRadius);
        }
    }
}
