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
        private InteriorMinimap _minimap;
        private BuildingDefinition _definition;
        private int _entryFloor;
        private readonly List<ulong> _playersInside = new();

        public Vector3 ReturnPosition => _returnPosition.Value;
        public Quaternion ReturnRotation => _returnRotation.Value;

        // ── Network lifecycle ──────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // The entrance fills InteriorSessionData before triggering the scene load.
                // If we spawn without a definition, the scene was opened some other way
                // (e.g., left open in the editor) — sit idle so we don't replicate junk.
                if (InteriorSessionData.Definition == null)
                {
                    Debug.LogWarning("[Interior] Bootstrapper spawned with no session data — idling.");
                    return;
                }

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

            // Spawn near the south wall (right next to the exit) and face +Z so the player
            // arrives looking into the interior with the exit door at their back.
            player.ServerTeleport(GetSpawnPosition(), Quaternion.identity);
            if (!_playersInside.Contains(clientId))
                _playersInside.Add(clientId);
        }

        // Spawn near the south wall of the Entry room. Entry sits on _entryFloor
        // (middle floor for multi-storey buildings with a basement).
        private Vector3 GetSpawnPosition()
        {
            if (spawnPoint != null) return spawnPoint.position;

            var def = ResolveDefinition();
            if (def == null) return _origin.Value + new Vector3(0f, 1f, 0f);

            float halfCell = def.GridCellMeters * 0.5f;
            float entryY   = _entryFloor * def.FloorHeightMeters;
            return _origin.Value + new Vector3(halfCell, entryY + 1f, 1.5f);
        }

        public void PlayerExited(ulong clientId)
        {
            if (!IsServer) return;
            _playersInside.Remove(clientId);
            if (_playersInside.Count != 0) return;

            // Roll fresh layouts for the next visit — clear all entrance seeds.
            foreach (var entrance in Object.FindObjectsByType<InteriorEntrance>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                entrance.ResetSeed();

            // Route through NetworkSceneTransitionService so its load-tracker stays
            // in sync — otherwise re-entering the building after exit takes a stale
            // "already loaded" branch and the bootstrapper never spawns again.
            var service = Object.FindFirstObjectByType<FriendSlop.SceneManagement.NetworkSceneTransitionService>(
                FindObjectsInactive.Exclude);
            if (service != null)
                service.ServerUnloadScenePath(gameObject.scene.path);
            else
                NetworkManager.SceneManager.UnloadScene(gameObject.scene);
        }

        // ── Generation pipeline ────────────────────────────────────────────────

        private IEnumerator GenerateAndTeleport(ulong requestingClientId)
        {
            InteriorEvents.SetLoading(true);
            yield return null;

            if (_definition != null)
            {
                var layout = InteriorLayoutGenerator.Generate(_definition, _seed.Value, SocketDirection.South);
                _entryFloor = layout.EntryFloor;
                _interiorRoot = BuildRooms(layout, _origin.Value);
                _minimap = InteriorMinimap.Spawn(layout, _definition, _origin.Value);
                yield return null;
                BakeNavMesh(_interiorRoot);
                yield return null;
                SpawnDoors(layout, _origin.Value);
                PositionExitDoor();
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

            var layout = InteriorLayoutGenerator.Generate(defAsset, _seed.Value, SocketDirection.South);
            _entryFloor = layout.EntryFloor;
            _interiorRoot = BuildRooms(layout, _origin.Value);
            _minimap = InteriorMinimap.Spawn(layout, defAsset, _origin.Value);
            PositionExitDoor();

            InteriorEvents.SetLoading(false);
        }

        // Position the exit door near the south wall of the entry room. Wraps a coroutine
        // so we can retry if the scene NetworkObjects haven't all spawned yet.
        private void PositionExitDoor() => StartCoroutine(PositionExitDoorWhenReady());

        private IEnumerator PositionExitDoorWhenReady()
        {
            InteriorExitDoor exit = null;
            for (int i = 0; i < 30 && exit == null; i++)
            {
                var all = Object.FindObjectsByType<InteriorExitDoor>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

                if (all.Length > 0)
                {
                    exit = all[0];
                    // Clean up any leftover duplicates from old scene saves.
                    for (int j = 1; j < all.Length; j++)
                        Destroy(all[j].gameObject);
                    break;
                }
                yield return null;
            }

            if (exit == null)
            {
                Debug.LogWarning("[Interior] Exit door not found in scene — was 'Repair Interior Scene' run?");
                yield break;
            }

            var def = ResolveDefinition();
            float halfCell = def != null ? def.GridCellMeters * 0.5f : 4f;
            float entryY   = def != null ? _entryFloor * def.FloorHeightMeters : 0f;
            // 0.15 m from the wall plane — slab back face lands flush with the wall pillars.
            var pos = _origin.Value + new Vector3(halfCell, entryY, 0.15f);
            exit.transform.position = pos;
            exit.transform.rotation = Quaternion.identity;
            Debug.Log($"[Interior] Exit door positioned at {pos} (in scene '{exit.gameObject.scene.name}')");
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
            // Move into the interior scene so rooms unload with the scene, not with the planet.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.position = origin;

            var def = ResolveDefinition();
            if (def == null) return root;

            foreach (var room in layout.Rooms)
            {
                if (room.Definition.Prefab == null) continue;
                var worldPos = InteriorLayoutGenerator.RoomWorldPosition(room, origin, def);
                var go = Object.Instantiate(room.Definition.Prefab, worldPos, Quaternion.identity, root.transform);
                go.name = $"{room.Definition.name} [{room.GridPosition}]";

                PatchUnconnectedSockets(go.transform, room, def);
            }
            return root;
        }

        // Plugs the door-frame opening for any socket the generator left unconnected.
        // Door openings live on the SW-most cell of each wall (matching BuildPerimeterWall).
        // Vertical sockets (Up/Down) leave 2×2 holes in ceiling/floor at the NW corner —
        // patch those too so the bottom/top of a stair stack doesn't open into the void.
        private static void PatchUnconnectedSockets(Transform roomRoot, PlacedRoom room, BuildingDefinition def)
        {
            const float doorWidth  = 2f;
            const float doorHeight = 3f;
            const float wall       = 0.2f;
            // Match BuildFloorOrCeiling.StairHoleW / StairHoleD in the editor builder.
            const float holeW      = 4f;
            const float holeD      = 5f;
            float c = def.GridCellMeters;
            float h = def.FloorHeightMeters;
            float w = room.Definition.GridSize.x * c;
            float d = room.Definition.GridSize.y * c;

            // If the room's Up socket is unconnected, the ramp leads to a patched ceiling
            // and is just a dead end — remove it so the player isn't confused.
            if (room.Definition.HasSocket(SocketDirection.Up) &&
                !room.ConnectedSockets.Contains(SocketDirection.Up))
            {
                var ramp = roomRoot.Find("Ramp");
                if (ramp != null) Object.Destroy(ramp.gameObject);
            }

            foreach (var s in room.Definition.Sockets)
            {
                if (room.ConnectedSockets.Contains(s)) continue;

                Vector3 localPos;
                Vector3 size;
                switch (s)
                {
                    case SocketDirection.North:
                        localPos = new Vector3(c * 0.5f, doorHeight * 0.5f, d);
                        size     = new Vector3(doorWidth, doorHeight, wall);
                        break;
                    case SocketDirection.South:
                        localPos = new Vector3(c * 0.5f, doorHeight * 0.5f, 0f);
                        size     = new Vector3(doorWidth, doorHeight, wall);
                        break;
                    case SocketDirection.East:
                        localPos = new Vector3(w, doorHeight * 0.5f, c * 0.5f);
                        size     = new Vector3(wall, doorHeight, doorWidth);
                        break;
                    case SocketDirection.West:
                        localPos = new Vector3(0f, doorHeight * 0.5f, c * 0.5f);
                        size     = new Vector3(wall, doorHeight, doorWidth);
                        break;
                    case SocketDirection.Up:
                        // Cap the ceiling hole at the NW corner (x=0..holeW, z=d-holeD..d).
                        localPos = new Vector3(holeW * 0.5f, h, d - holeD * 0.5f);
                        size     = new Vector3(holeW, wall, holeD);
                        break;
                    case SocketDirection.Down:
                        // Cap the floor hole at the same NW corner so the player can't
                        // fall through the bottom of a stair stack.
                        localPos = new Vector3(holeW * 0.5f, 0f, d - holeD * 0.5f);
                        size     = new Vector3(holeW, wall, holeD);
                        break;
                    default: continue;
                }

                var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                patch.name = $"WallPatch_{s}";
                patch.transform.SetParent(roomRoot, false);
                patch.transform.localPosition = localPos;
                patch.transform.localScale    = size;
            }
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
                Debug.Log($"[Interior] Door at {pos} rot={rot.eulerAngles} " +
                          $"(RoomA grid={conn.RoomA.GridPosition} size={conn.RoomA.Definition.GridSize} socket={conn.SocketA})");
                var door = Object.Instantiate(doorPrefab, pos, rot);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(door, gameObject.scene);
                ApplyDoorColor(door);
                var netObj = door.GetComponent<NetworkObject>();
                if (netObj != null) netObj.Spawn(destroyWithScene: true);
            }
        }

        // Saddle brown so doors stand out from the grey wall material.
        private static readonly Color DoorColor = new(0.55f, 0.3f, 0.1f);
        private static void ApplyDoorColor(GameObject door)
        {
            var renderer = door.GetComponentInChildren<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) return;
            var mat = new Material(renderer.sharedMaterial) { color = DoorColor };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", DoorColor);
            renderer.sharedMaterial = mat;
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
            if (_minimap != null)
            {
                Destroy(_minimap.gameObject);
                _minimap = null;
            }
        }
    }
}
