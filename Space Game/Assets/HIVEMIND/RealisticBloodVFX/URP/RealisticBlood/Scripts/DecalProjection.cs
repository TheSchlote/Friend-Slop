using UnityEngine;
using System.Collections.Generic; // <-- List için gerekli

public class UniversalDecalSpawner : MonoBehaviour
{
    public GameObject[] decalPrefabs;
    public GameObject vfxPrefab; // Splash VFX Graph
    public bool isHDRP = false;

    [Header("Random Size Range")]
    public float minScale = 0.5f;
    public float maxScale = 1.5f;

    [Header("Random Lifetime Range (seconds)")]
    public float minLifetime = 5f;
    public float maxLifetime = 15f;

    private ParticleSystem ps;
    private List<ParticleCollisionEvent> collisionEvents; // <-- Array yerine List

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        collisionEvents = new List<ParticleCollisionEvent>();
    }

    void OnParticleCollision(GameObject other)
    {
        int count = ps.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < count; i++)
        {
            if (decalPrefabs == null || decalPrefabs.Length == 0)
                return;

            Vector3 pos = collisionEvents[i].intersection;
            Vector3 normal = collisionEvents[i].normal;

            // Calculate rotation
            Quaternion baseRotation = Quaternion.LookRotation(isHDRP ? -normal : normal);
            Quaternion randomSpin = Quaternion.AngleAxis(Random.Range(0f, 360f), normal);
            Quaternion finalRotation = randomSpin * baseRotation;

            Vector3 spawnPos = pos + normal * 0.01f;

            // ----------- Spawn Decal -----------
            GameObject decalPrefab = decalPrefabs[Random.Range(0, decalPrefabs.Length)];
            GameObject decal = Instantiate(decalPrefab, spawnPos, finalRotation);

            float scale = Random.Range(minScale, maxScale);
            bool didScale = false;

#if UNITY_RENDER_PIPELINE_HIGH_DEFINITION
            if (isHDRP)
            {
                var hdrpProjector = decal.GetComponent<UnityEngine.Rendering.HighDefinition.DecalProjector>();
                if (hdrpProjector != null)
                {
                    var original = hdrpProjector.size;
                    hdrpProjector.size = new Vector3(scale, scale, original.z);
                    didScale = true;
                }
            }
#endif

#if UNITY_RENDER_PIPELINE_UNIVERSAL
            if (!isHDRP)
            {
                var urpProjector = decal.GetComponent<UnityEngine.Rendering.Universal.DecalProjector>();
                if (urpProjector != null)
                {
                    decal.transform.localScale = Vector3.one * scale;
                    didScale = true;
                }
            }
#endif

            if (!didScale)
            {
                decal.transform.localScale = Vector3.one * scale;
            }

            float life = Random.Range(minLifetime, maxLifetime);
            Destroy(decal, life);

            // ----------- Spawn VFX Splash -----------
            if (vfxPrefab != null)
            {
                GameObject vfxInstance = Instantiate(vfxPrefab, spawnPos, Quaternion.LookRotation(normal));
                Destroy(vfxInstance, 3f); // Shorter splash life (adjust if needed)
            }
        }
    }
}
