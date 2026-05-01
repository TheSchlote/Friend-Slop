using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Hazards
{
    public class AnomalySpawner : MonoBehaviour
    {
        [SerializeField] private GameObject[] anomalyPrefabs = new GameObject[0];
        [SerializeField] private int maxOrbs = 3;
        [SerializeField] private float minSpawnInterval = 30f;
        [SerializeField] private float maxSpawnInterval = 60f;

        private float _nextSpawnTime;
        private bool _wasRoundActive;
        private readonly List<AnomalyOrb> _activeOrbs = new();

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (RoundManager.Instance == null) return;

            var roundActive = RoundManager.Instance.Phase.Value == RoundPhase.Active;

            if (!_wasRoundActive && roundActive)
                _nextSpawnTime = Time.time + Random.Range(minSpawnInterval * 0.4f, minSpawnInterval);

            _wasRoundActive = roundActive;

            if (!roundActive) return;

            if (Time.time >= _nextSpawnTime)
            {
                _nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
                TrySpawnOrb();
            }
        }

        private void TrySpawnOrb()
        {
            if (anomalyPrefabs.Length == 0) return;

            var liveCount = 0;
            foreach (var active in _activeOrbs)
            {
                if (active != null && active.IsSpawned) liveCount++;
            }
            if (liveCount >= maxOrbs) return;

            var world = GetCurrentPlanet();
            if (world == null) return;

            var spawnPos = world.GetSurfacePoint(Random.onUnitSphere, 2f);
            var prefab = anomalyPrefabs[Random.Range(0, anomalyPrefabs.Length)];
            if (prefab == null) return;

            var go = Instantiate(prefab, spawnPos, Quaternion.identity);
            go.GetComponent<NetworkObject>().Spawn();
            var orb = go.GetComponent<AnomalyOrb>();
            orb.ServerInitialize(spawnPos);
            _activeOrbs.Add(orb);
        }

        private static SphereWorld GetCurrentPlanet()
        {
            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player != null && player.IsSpawned)
                    return SphereWorld.GetClosest(player.transform.position);
            }
            return SphereWorld.GetClosest(Vector3.zero);
        }
    }
}
