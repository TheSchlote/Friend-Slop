using FriendSlop.Core;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Hazards
{
    // Scene-local hazard director. Lives in a planet scene, watches the round phase, and
    // rains meteors onto the planet only while that planet is active and the round is in
    // play. Server-only: spawning is gated by NetworkManager.IsServer.
    public class MeteorShower : MonoBehaviour
    {
        [SerializeField] private MeteorHazard meteorPrefab;
        [SerializeField] private PlanetEnvironment planetEnvironment;
        [SerializeField] private SphereWorld sphereWorld;

        [Header("Spawn Cadence")]
        [SerializeField, Min(0.05f)] private float minSpawnInterval = 1.5f;
        [SerializeField, Min(0.05f)] private float maxSpawnInterval = 3.0f;
        [SerializeField, Min(1)] private int meteorsPerWave = 1;
        [SerializeField, Min(0f)] private float startupDelay = 4f;

        [Header("Spawn Geometry")]
        [SerializeField, Min(1f)] private float spawnAltitude = 38f;

        private float _nextSpawnTime;
        private bool _wasActive;
        private bool _hasSeededTimer;

        private void OnValidate()
        {
            if (maxSpawnInterval < minSpawnInterval)
                maxSpawnInterval = minSpawnInterval;
        }

        private void Awake()
        {
            if (planetEnvironment == null)
                planetEnvironment = GetComponentInParent<PlanetEnvironment>(true);
            if (sphereWorld == null && planetEnvironment != null)
                sphereWorld = planetEnvironment.SphereWorld;
            if (sphereWorld == null)
                sphereWorld = GetComponentInParent<SphereWorld>(true);
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (meteorPrefab == null) return;

            var round = RoundManagerRegistry.Current;
            if (round == null) return;

            var canSpawn = ShouldShowerNow(round);
            if (!canSpawn)
            {
                _wasActive = false;
                _hasSeededTimer = false;
                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;
                _nextSpawnTime = Time.time + Mathf.Max(0f, startupDelay);
                _hasSeededTimer = true;
            }

            if (!_hasSeededTimer)
            {
                _nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
                _hasSeededTimer = true;
            }

            if (Time.time < _nextSpawnTime) return;

            for (var i = 0; i < meteorsPerWave; i++)
                TrySpawnMeteor(round);

            _nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
        }

        private bool ShouldShowerNow(RoundManager round)
        {
            if (round.Phase.Value != RoundPhase.Active) return false;
            // Bind to a specific planet env so a shower in a now-inactive planet scene
            // doesn't keep spawning meteors during another planet's round.
            if (planetEnvironment != null && !round.IsEnvironmentActiveForCurrentPlanet(planetEnvironment))
                return false;
            return true;
        }

        private void TrySpawnMeteor(RoundManager round)
        {
            var world = sphereWorld != null ? sphereWorld : SphereWorld.GetClosest(transform.position);
            if (world == null) return;

            var direction = PickSpawnDirection();
            var spawnPos = world.GetSurfacePoint(direction, spawnAltitude);

            var meteor = Instantiate(meteorPrefab, spawnPos, Quaternion.identity);
            // Move into the planet scene so unloading the planet also tears down the meteor.
            var targetScene = planetEnvironment != null ? planetEnvironment.gameObject.scene : gameObject.scene;
            if (targetScene.IsValid() && targetScene.isLoaded && meteor.gameObject.scene != targetScene)
                SceneManager.MoveGameObjectToScene(meteor.gameObject, targetScene);

            var netObj = meteor.GetComponent<NetworkObject>();
            if (netObj == null) { Destroy(meteor.gameObject); return; }
            netObj.Spawn();
            meteor.ServerInitialize();
        }

        private static Vector3 PickSpawnDirection()
        {
            var dir = Random.onUnitSphere;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
        }
    }
}
