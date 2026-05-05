using FriendSlop.Core;
using FriendSlop.Player;
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

        [Header("Player Targeting")]
        // Fraction of meteors that aim at or near a random active player's current position.
        // The remainder spawn at a uniformly random direction. 0 = pure random, 1 = always aim.
        [SerializeField, Range(0f, 1f)] private float playerTargetingChance = 0.3f;
        // Inner radius of the jitter ring around a targeted player. Meteors rarely land
        // closer than this, so a player standing still doesn't get hit dead-on every time.
        [SerializeField, Min(0f)] private float playerTargetJitterMinRadius = 2f;
        // Outer radius of the jitter ring. Most player-targeted meteors land somewhere in
        // the [min, max] annulus around the player, biased outward by area-uniform sampling.
        [SerializeField, Min(0f)] private float playerTargetJitterRadius = 8f;
        // Probability that a player-targeted meteor ignores the inner radius and lands
        // right on top of the player. Keep low - this is the rare "scary" hit.
        [SerializeField, Range(0f, 1f)] private float playerTargetDirectHitChance = 0.12f;

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

            var direction = PickSpawnDirection(world);
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

        private Vector3 PickSpawnDirection(SphereWorld world)
        {
            // Weighted choice: half the time aim at (or near) a random active player; the
            // rest of the time pick a uniformly random surface direction.
            if (Random.value < playerTargetingChance)
            {
                if (TryPickPlayerTargetDirection(world, out var targeted))
                    return targeted;
            }
            return RandomUnitDirection();
        }

        private bool TryPickPlayerTargetDirection(SphereWorld world, out Vector3 direction)
        {
            direction = default;
            var players = NetworkFirstPersonController.ActivePlayers;
            if (players == null || players.Count == 0) return false;

            // Reservoir-style sampling, skipping nulls/dead/unspawned. Avoids allocating a
            // filtered list every spawn while still distributing fairly.
            NetworkFirstPersonController chosen = null;
            var live = 0;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || !p.IsSpawned || p.IsDead) continue;
                live++;
                if (Random.Range(0, live) == 0) chosen = p;
            }
            if (chosen == null) return false;

            var center = world.Center;
            var aim = chosen.transform.position;
            // Annulus jitter on the surface tangent plane: pick a direction, then a radius
            // in [min, max] sampled area-uniformly so the spread feels natural rather than
            // ringed. A small fraction of meteors skip the inner radius and land on top of
            // the player to keep the targeting threatening.
            if (playerTargetJitterRadius > 0f)
            {
                var up = world.GetUp(aim);
                var tangent = Vector3.Cross(up, Vector3.right);
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(up, Vector3.forward);
                tangent.Normalize();
                var bitangent = Vector3.Cross(up, tangent).normalized;

                var minR = Random.value < playerTargetDirectHitChance
                    ? 0f
                    : Mathf.Min(playerTargetJitterMinRadius, playerTargetJitterRadius);
                var maxR = Mathf.Max(minR, playerTargetJitterRadius);
                // Area-uniform radius: sqrt(uniform(minR^2, maxR^2)).
                var radius = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
                var angle = Random.Range(0f, Mathf.PI * 2f);
                var offX = Mathf.Cos(angle) * radius;
                var offY = Mathf.Sin(angle) * radius;
                aim += tangent * offX + bitangent * offY;
            }

            var dir = aim - center;
            if (dir.sqrMagnitude < 0.001f) return false;
            direction = dir.normalized;
            return true;
        }

        private static Vector3 RandomUnitDirection()
        {
            var dir = Random.onUnitSphere;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
        }
    }
}
