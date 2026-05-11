using System.Collections;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Interiors
{
    // Placed on a building exterior. When any player enters the trigger the server seeds
    // the layout; all clients receive the seed via NetworkVariable and generate identically.
    // Rooms are local GameObjects; doors are server-spawned NetworkObjects.
    [RequireComponent(typeof(Collider))]
    public class InteriorSpawner : NetworkBehaviour
    {
        [SerializeField] private BuildingDefinition definition;
        [SerializeField] private GameObject doorPrefab;

        private readonly NetworkVariable<int> _layoutSeed =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameObject _interiorRoot;
        private NavMeshSurface _navSurface;
        private bool _generating;

        // ── Network lifecycle ──────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            _layoutSeed.OnValueChanged += OnSeedChanged;

            // Late-joining clients: seed may already be set.
            if (_layoutSeed.Value >= 0)
                StartCoroutine(GenerateInterior(_layoutSeed.Value));
        }

        public override void OnNetworkDespawn()
        {
            _layoutSeed.OnValueChanged -= OnSeedChanged;
            DestroyInterior();
        }

        // ── Trigger detection (server-only physics) ────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            if (_layoutSeed.Value >= 0) return;          // already seeded
            if (!other.CompareTag("Player")) return;

            // Seed from position for per-building determinism.
            int seed = (Mathf.RoundToInt(transform.position.x) * 31
                      + Mathf.RoundToInt(transform.position.z)) & int.MaxValue;
            _layoutSeed.Value = seed;
        }

        // ── Generation pipeline ────────────────────────────────────────────────

        private void OnSeedChanged(int _, int newSeed)
        {
            if (newSeed < 0 || _generating) return;
            StartCoroutine(GenerateInterior(newSeed));
        }

        private IEnumerator GenerateInterior(int seed)
        {
            if (definition == null || _interiorRoot != null) yield break;
            _generating = true;

            InteriorEvents.SetLoading(true);
            yield return null; // let the fade start

            var layout = InteriorLayoutGenerator.Generate(definition, seed);

            _interiorRoot = new GameObject("Interior");
            _interiorRoot.transform.SetParent(transform, worldPositionStays: false);

            SpawnRooms(layout);

            yield return null; // let rooms finish rendering

            BakeNavMesh();

            yield return null;

            if (IsServer)
                SpawnDoors(layout);

            InteriorEvents.SetLoading(false);
            _generating = false;
        }

        // ── Room instantiation ─────────────────────────────────────────────────

        private void SpawnRooms(InteriorLayout layout)
        {
            var origin = transform.position;
            foreach (var room in layout.Rooms)
            {
                if (room.Definition.Prefab == null) continue;
                var worldPos = InteriorLayoutGenerator.RoomWorldPosition(room, origin, definition);
                var go = Instantiate(room.Definition.Prefab, worldPos, Quaternion.identity, _interiorRoot.transform);
                go.name = $"{room.Definition.name} [{room.GridPosition.x},{room.GridPosition.y},{room.GridPosition.z}]";
            }
        }

        // ── NavMesh bake ───────────────────────────────────────────────────────

        private void BakeNavMesh()
        {
            _navSurface = _interiorRoot.AddComponent<NavMeshSurface>();
            _navSurface.collectObjects = CollectObjects.Children;
            _navSurface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            _navSurface.BuildNavMesh();
        }

        // ── Door spawning (server only) ────────────────────────────────────────

        private void SpawnDoors(InteriorLayout layout)
        {
            if (doorPrefab == null) return;
            var origin = transform.position;
            foreach (var conn in layout.Connections)
            {
                if (conn.SocketA.IsVertical()) continue; // stairs/elevators handle their own
                var (pos, rot) = InteriorLayoutGenerator.DoorTransform(conn, origin, definition);
                var door = Instantiate(doorPrefab, pos, rot);
                var netObj = door.GetComponent<NetworkObject>();
                if (netObj != null) netObj.Spawn(destroyWithScene: true);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────────

        private void DestroyInterior()
        {
            if (_interiorRoot != null)
            {
                Destroy(_interiorRoot);
                _interiorRoot = null;
            }
        }
    }
}
