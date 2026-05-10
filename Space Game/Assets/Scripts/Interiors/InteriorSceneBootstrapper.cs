using System.Collections;
using System.Collections.Generic;
using FriendSlop.Player;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Interiors
{
    // Placed in Building_Interior.unity. Reads InteriorSessionData on the server,
    // replicates seed/origin to clients, then all clients generate rooms locally.
    // Server also spawns door NetworkObjects and teleports the requesting player in.
    public class InteriorSceneBootstrapper : NetworkBehaviour
    {
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private GameObject doorPrefab;
        [SerializeField] private InteriorCatalog catalog;

        private readonly NetworkVariable<int> _seed =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _definitionIndex =
            new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _origin =
            new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _returnPosition =
            new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Quaternion> _returnRotation =
            new(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameObject _interiorRoot;
        private BuildingDefinition _definition;
        private readonly List<ulong> _playersInside = new();

        public Vector3 ReturnPosition => _returnPosition.Value;
        public Quaternion ReturnRotation => _returnRotation.Value;

        // ── Network lifecycle ──────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _seed.Value           = InteriorSessionData.Seed;
                _origin.Value         = InteriorSessionData.InteriorWorldOrigin;
                _returnPosition.Value = InteriorSessionData.ReturnPosition;
                _returnRotation.Value = InteriorSessionData.ReturnRotation;
                _definition           = InteriorSessionData.Definition;
                _definitionIndex.Value = FindDefinitionIndex(InteriorSessionData.Definition);

                StartCoroutine(GenerateAndTeleport(InteriorSessionData.RequestingClientId));
            }
            else
            {
                if (_seed.Value >= 0)
                    StartCoroutine(GenerateLocal());
                else
                    _seed.OnValueChanged += OnSeedReceived;
            }
        }

        public override void OnNetworkDespawn()
        {
            _seed.OnValueChanged -= OnSeedReceived;
            DestroyInterior();
        }

        // ── Public API for InteriorEntrance (scene already loaded case) ────────

        public void TeleportPlayerIn(ulong clientId)
        {
            if (!IsServer) return;
            var player = FindPlayer(clientId);
            if (player == null) return;

            var dest = spawnPoint != null ? spawnPoint.position : _origin.Value + Vector3.up * 2f;
            player.ServerTeleport(dest, _returnRotation.Value);
            if (!_playersInside.Contains(clientId))
                _playersInside.Add(clientId);
        }

        public void PlayerExited(ulong clientId)
        {
            if (!IsServer) return;
            _playersInside.Remove(clientId);
            if (_playersInside.Count == 0)
                NetworkManager.SceneManager.UnloadScene(gameObject.scene);
        }

        // ── Generation pipeline ────────────────────────────────────────────────

        private IEnumerator GenerateAndTeleport(ulong requestingClientId)
        {
            InteriorEvents.SetLoading(true);
            yield return null;

            if (_definition != null)
            {
                var layout = InteriorLayoutGenerator.Generate(_definition, _seed.Value);
                _interiorRoot = BuildRooms(layout, _origin.Value);
                yield return null;
                BakeNavMesh(_interiorRoot);
                yield return null;
                SpawnDoors(layout, _origin.Value);
            }

            InteriorEvents.SetLoading(false);

            TeleportPlayerIn(requestingClientId);
        }

        private IEnumerator GenerateLocal()
        {
            // Wait one frame for server NetworkVariables to stabilise.
            yield return null;

            var defAsset = ResolveDefinition();
            if (defAsset == null || _seed.Value < 0) yield break;

            InteriorEvents.SetLoading(true);
            yield return null;

            var layout = InteriorLayoutGenerator.Generate(defAsset, _seed.Value);
            _interiorRoot = BuildRooms(layout, _origin.Value);

            InteriorEvents.SetLoading(false);
        }

        private void OnSeedReceived(int _, int newSeed)
        {
            if (newSeed < 0) return;
            _seed.OnValueChanged -= OnSeedReceived;
            StartCoroutine(GenerateLocal());
        }

        // ── Room / door helpers ────────────────────────────────────────────────

        private GameObject BuildRooms(InteriorLayout layout, Vector3 origin)
        {
            var root = new GameObject("Interior_Rooms");
            root.transform.position = origin;

            var def = ResolveDefinition();
            if (def == null) return root;

            foreach (var room in layout.Rooms)
            {
                if (room.Definition.Prefab == null) continue;
                var worldPos = InteriorLayoutGenerator.RoomWorldPosition(room, origin, def);
                var go = Object.Instantiate(room.Definition.Prefab, worldPos, Quaternion.identity, root.transform);
                go.name = $"{room.Definition.name} [{room.GridPosition}]";
            }
            return root;
        }

        private static void BakeNavMesh(GameObject root)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        private void SpawnDoors(InteriorLayout layout, Vector3 origin)
        {
            if (doorPrefab == null) return;
            var def = ResolveDefinition();
            if (def == null) return;

            foreach (var conn in layout.Connections)
            {
                if (conn.SocketA.IsVertical()) continue;
                var (pos, rot) = InteriorLayoutGenerator.DoorTransform(conn, origin, def);
                var door = Object.Instantiate(doorPrefab, pos, rot);
                var netObj = door.GetComponent<NetworkObject>();
                if (netObj != null) netObj.Spawn(destroyWithScene: true);
            }
        }

        // ── Utility ───────────────────────────────────────────────────────────

        // On the server _definition is set directly; on clients look it up via the replicated index.
        private BuildingDefinition ResolveDefinition()
        {
            if (_definition != null) return _definition;
            if (catalog == null || _definitionIndex.Value < 0) return null;
            if (_definitionIndex.Value >= catalog.Buildings.Count) return null;
            return catalog.Buildings[_definitionIndex.Value];
        }

        private int FindDefinitionIndex(BuildingDefinition def)
        {
            if (catalog == null || def == null) return -1;
            for (int i = 0; i < catalog.Buildings.Count; i++)
                if (catalog.Buildings[i] == def) return i;
            return -1;
        }

        private static NetworkFirstPersonController FindPlayer(ulong clientId)
        {
            foreach (var obj in Object.FindObjectsByType<NetworkFirstPersonController>(FindObjectsSortMode.None))
                if (obj.OwnerClientId == clientId) return obj;
            return null;
        }

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
