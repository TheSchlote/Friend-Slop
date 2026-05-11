using UnityEngine;

namespace FriendSlop.Effects
{
    // Static helper that spawns blood VFX at a hit point using prefabs from
    // a BloodVfxLibrary ScriptableObject loaded from Resources/. The library
    // wires up HIVEMIND RealisticBloodVFX (URP) decal + particle prefabs.
    public static class BloodSplatter
    {
        private const string LibraryResourcePath = "Effects/BloodVfxLibrary";

        private static BloodVfxLibrary _library;
        private static bool _libraryLoadAttempted;

        private static BloodVfxLibrary Library
        {
            get
            {
                if (_library != null) return _library;
                if (_libraryLoadAttempted) return null;
                _libraryLoadAttempted = true;
                _library = Resources.Load<BloodVfxLibrary>(LibraryResourcePath);
                if (_library == null)
                    Debug.LogWarning($"BloodSplatter: missing Resources/{LibraryResourcePath} — no blood VFX will spawn.");
                return _library;
            }
        }

        public static void Spawn(Vector3 position, Vector3 surfaceNormal, int count)
        {
            Spawn(position, position, surfaceNormal, count, isDeath: false);
        }

        public static void Spawn(Vector3 position, Vector3 surfaceNormal, int count, bool isDeath)
        {
            Spawn(position, position, surfaceNormal, count, isDeath);
        }

        // particleOrigin: where the burst spray comes from (e.g. the player's chest).
        // groundPosition: where the floor decals scatter (e.g. the surface beneath the player).
        public static void Spawn(Vector3 particleOrigin, Vector3 groundPosition, Vector3 surfaceNormal, int count, bool isDeath)
        {
            var lib = Library;
            if (lib == null || count <= 0) return;

            var n = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;

            var tangent = Vector3.Cross(n, Vector3.right);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.Cross(n, Vector3.forward);
            tangent.Normalize();
            var bitangent = Vector3.Cross(n, tangent).normalized;

            var burst = lib.PickParticleBurst();
            if (burst != null)
            {
                // Align the prefab's local +Y with the surface normal so cone-shape
                // emitters (which default to local Y) spray outward from the player.
                // Random twist around that axis for variety.
                var burstRot = Quaternion.FromToRotation(Vector3.up, n)
                             * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
                var burstGo = Object.Instantiate(burst, particleOrigin, burstRot);
                Object.Destroy(burstGo, lib.lifetime);
            }

            for (var i = 0; i < count; i++)
            {
                var useDeath = isDeath && i == 0;
                var prefab = useDeath ? lib.PickDeathDecal() : lib.PickDecal();
                if (prefab == null) continue;

                var radius = Random.Range(lib.decalOffsetRange.x, lib.decalOffsetRange.y);
                var ang = Random.Range(0f, Mathf.PI * 2f);
                var offset = (tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang)) * radius;
                var spotPos = groundPosition + offset + n * 0.02f;

                // URP DecalProjector projects along the GameObject's forward (+Z).
                // Aim forward into the surface and add a random Z roll for variety.
                var rot = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var go = Object.Instantiate(prefab, spotPos, rot);
                var s = Random.Range(lib.decalScaleRange.x, lib.decalScaleRange.y);
                if (useDeath) s *= lib.deathDecalSizeMultiplier;
                go.transform.localScale = go.transform.localScale * s;
                Object.Destroy(go, lib.lifetime);
            }
        }
    }
}
