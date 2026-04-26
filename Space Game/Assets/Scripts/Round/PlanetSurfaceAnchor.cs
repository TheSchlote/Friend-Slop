using UnityEngine;

namespace FriendSlop.Round
{
    // Marks a GameObject as belonging to a planet's surface so PlanetEnvironment can snap it
    // to the correct radius. Use for assets that aren't already referenced on PlanetEnvironment
    // (beacons, decor, signs). Launchpads and player spawns are handled directly via the env's
    // own references and don't need this component.
    [DisallowMultipleComponent]
    public class PlanetSurfaceAnchor : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float surfaceOffset;
        [SerializeField] private bool alignRotation = true;

        public float SurfaceOffset => surfaceOffset;
        public bool AlignRotation => alignRotation;

        public void SetSurfaceOffset(float value) => surfaceOffset = Mathf.Max(0f, value);
        public void SetAlignRotation(bool value) => alignRotation = value;
    }
}
