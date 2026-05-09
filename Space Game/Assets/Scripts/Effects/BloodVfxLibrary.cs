using UnityEngine;

namespace FriendSlop.Effects
{
    [CreateAssetMenu(menuName = "Friend Slop/Blood VFX Library", fileName = "BloodVfxLibrary")]
    public class BloodVfxLibrary : ScriptableObject
    {
        [Tooltip("Decal prefabs used per scattered spot. Picked uniformly at random per spot.")]
        public GameObject[] decalPrefabs;

        [Tooltip("Particle-burst prefabs spawned once at the impact point. Optional.")]
        public GameObject[] particleBurstPrefabs;

        [Tooltip("Larger 'death' decals used for the first spot of a death splat. Falls back to decalPrefabs when empty.")]
        public GameObject[] deathDecalPrefabs;

        [Tooltip("Min/max scale multiplier applied to each decal instance.")]
        public Vector2 decalScaleRange = new Vector2(0.7f, 1.3f);

        [Tooltip("Death decals also get this size multiplier on top of decalScaleRange.")]
        public float deathDecalSizeMultiplier = 2.2f;

        [Tooltip("Min/max radial offset (along the surface) where each spot is placed, in metres.")]
        public Vector2 decalOffsetRange = new Vector2(0f, 0.45f);

        [Tooltip("How long each spawned decal/particle GameObject lives, in seconds.")]
        public float lifetime = 30f;

        public GameObject PickDecal()
        {
            return PickRandom(decalPrefabs);
        }

        public GameObject PickDeathDecal()
        {
            return deathDecalPrefabs != null && deathDecalPrefabs.Length > 0
                ? PickRandom(deathDecalPrefabs)
                : PickDecal();
        }

        public GameObject PickParticleBurst()
        {
            return PickRandom(particleBurstPrefabs);
        }

        private static GameObject PickRandom(GameObject[] arr)
        {
            if (arr == null || arr.Length == 0) return null;
            return arr[Random.Range(0, arr.Length)];
        }
    }
}
