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
    public partial class InteriorSceneBootstrapper : NetworkBehaviour
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
        private readonly Dictionary<PlacedRoom, GameObject> _roomGoMap = new();
        private readonly List<ulong> _playersInside = new();

        public Vector3 ReturnPosition => _returnPosition.Value;
        public Quaternion ReturnRotation => _returnRotation.Value;

        // Ã¢â€â‚¬Ã¢â€â‚¬ Network lifecycle Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // The entrance fills InteriorSessionData before triggering the scene load.
                // If we spawn without a definition, the scene was opened some other way
                // (e.g., left open in the editor) Ã¢â‚¬â€ sit idle so we don't replicate junk.
                if (InteriorSessionData.Definition == null)
                {
                    Debug.LogWarning("[Interior] Bootstrapper spawned with no session data Ã¢â‚¬â€ idling.");
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

        // Ã¢â€â‚¬Ã¢â€â‚¬ Public API for InteriorEntrance (scene already loaded case) Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

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

            // Roll fresh layouts for the next visit Ã¢â‚¬â€ clear all entrance seeds.
            foreach (var entrance in Object.FindObjectsByType<InteriorEntrance>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                entrance.ResetSeed();

            // Route through NetworkSceneTransitionService so its load-tracker stays
            // in sync Ã¢â‚¬â€ otherwise re-entering the building after exit takes a stale
            // "already loaded" branch and the bootstrapper never spawns again.
            var service = Object.FindFirstObjectByType<FriendSlop.SceneManagement.NetworkSceneTransitionService>(
                FindObjectsInactive.Exclude);
            if (service != null)
                service.ServerUnloadScenePath(gameObject.scene.path);
            else
                NetworkManager.SceneManager.UnloadScene(gameObject.scene);
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ Generation pipeline Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        private IEnumerator GenerateAndTeleport(ulong requestingClientId)
        {
            InteriorEvents.SetLoading(true);
            yield return null;

            if (_definition != null)
            {
                // Blueprint mode: bypass the procedural generator and materialise the
                // designer-authored layout instead. Definition is still used for cell
                // size, theme, etc. Set by BlueprintEntrance before scene load.
                InteriorLayout layout;
                var blueprint = InteriorSessionData.Blueprint;
                if (blueprint != null)
                {
                    layout = FriendSlop.Interiors.Blueprints.BlueprintLayoutBuilder.Build(blueprint, _definition);
                    Debug.Log($"[Interior] Loading from blueprint '{blueprint.DisplayName}': " +
                              $"{layout.Rooms.Count} rooms, {layout.Connections.Count} connections.");
                }
                else
                {
                    layout = InteriorLayoutGenerator.Generate(_definition, _seed.Value, SocketDirection.South);
                }
                _entryFloor = layout.EntryFloor;
                _interiorRoot = BuildRooms(layout, _origin.Value);
                _minimap = InteriorMinimap.Spawn(layout, _definition, _origin.Value);
                yield return null;
                SpawnDoors(layout, _origin.Value);
                yield return null;
                // Furniture must be placed BEFORE the NavMesh bake so its colliders become
                // obstacles. Stripping the bake order: rooms Ã¢â€ â€™ doors Ã¢â€ â€™ furniture Ã¢â€ â€™ bake.
                SpawnFurniture(layout);
                yield return null;
                BakeNavMesh(_interiorRoot);
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
            SpawnFurniture(layout);
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
                Debug.LogWarning("[Interior] Exit door not found in scene Ã¢â‚¬â€ was 'Repair Interior Scene' run?");
                yield break;
            }

            var def = ResolveDefinition();
            float halfCell = def != null ? def.GridCellMeters * 0.5f : 1.7f;
            float entryY   = def != null ? _entryFloor * def.FloorHeightMeters : 0f;
            // 0.15 m from the wall plane Ã¢â‚¬â€ slab back face lands flush with the wall pillars.
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

        // Ã¢â€â‚¬Ã¢â€â‚¬ Room / door helpers Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        private GameObject BuildRooms(InteriorLayout layout, Vector3 origin)
        {
            var root = new GameObject("Interior_Rooms");
            // Move into the interior scene so rooms unload with the scene, not with the planet.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.position = origin;

            var def = ResolveDefinition();
            if (def == null) return root;

            // One tinted material instance, shared across every renderer in this building.
            Material themed = CreateThemedMaterial(def);

            // Reset the persistent roomÃ¢â€ â€™GO map; SpawnFurniture reads it after rooms exist.
            _roomGoMap.Clear();

            // Sanity pass Ã¢â‚¬â€ any registered connection whose two rooms don't actually share
            // an edge at the connection's socket leaves a door cell open to the void on
            // RoomA's side (because PatchUnconnectedSockets sees the socket as connected
            // and skips the patch, but no neighbor wall is there to back it up). Remove
            // the bad socket from each room's ConnectedSockets so the patch pass plugs it.
            // Excludes the building's reserved exit socket (intentionally has no neighbor).
            for (int i = 0; i < layout.Connections.Count; i++)
            {
                var c = layout.Connections[i];
                if (c.SocketA.IsVertical()) continue;
                if (c.RoomA == layout.ExitRoom && layout.ExitSocket.HasValue && c.SocketA == layout.ExitSocket.Value) continue;
                if (c.RoomB == layout.ExitRoom && layout.ExitSocket.HasValue && c.SocketB == layout.ExitSocket.Value) continue;
                if (RoomsShareEdge(c.RoomA, c.RoomB, c.SocketA)) continue;
                Debug.LogWarning($"[Interiors] Connection {c.RoomA.Definition.name}@{c.RoomA.GridPosition} <-> {c.RoomB.Definition.name}@{c.RoomB.GridPosition} via {c.SocketA} doesn't share a grid edge Ã¢â‚¬â€ plugging the door cell instead of opening to the void.");
                c.RoomA.ConnectedSockets.Remove(c.SocketA);
                c.RoomB.ConnectedSockets.Remove(c.SocketB);
            }

            foreach (var room in layout.Rooms)
            {
                if (room.Definition.Prefab == null) continue;
                var worldPos = InteriorLayoutGenerator.RoomWorldPosition(room, origin, def);
                // Rotating around the prefab's SW-corner pivot shifts the room out of the
                // +X+Z quadrant. Compensate so the rotated room's SW corner stays at the
                // grid-position world coords.
                var rotation = Quaternion.Euler(0f, room.Rotation * 90f, 0f);
                worldPos += RotationTranslation(room, def.GridCellMeters);
                var go = Object.Instantiate(room.Definition.Prefab, worldPos, rotation, root.transform);
                go.name = $"{room.Definition.name} [{room.GridPosition}, r{room.Rotation}]";

                PatchUnconnectedSockets(go.transform, room, def);
                TryAddGarageDoor(go.transform, room, def, layout);
                if (themed != null) ApplyThemedMaterial(go, themed);
                _roomGoMap[room] = go;
            }

            // Strip wall geometry for open-passage connections so the two rooms read as one
            // continuous space (no door, no wall, no door-frame pillars). Also remove any
            // anchors against the stripped wall Ã¢â‚¬â€ they'd be floating in air.
            foreach (var conn in layout.Connections)
            {
                if (!conn.IsOpenPassage) continue;
                Debug.Log($"[Interiors] Archway {conn.RoomA.Definition.name}@{conn.RoomA.GridPosition}r{conn.RoomA.Rotation}/{conn.SocketA} <-> {conn.RoomB.Definition.name}@{conn.RoomB.GridPosition}r{conn.RoomB.Rotation}/{conn.SocketB}");
                if (!_roomGoMap.TryGetValue(conn.RoomA, out var goA)) { Debug.LogWarning($"[Interiors]   Ã¢â€ â€˜ RoomA missing from _roomGoMap (Prefab null?)"); continue; }
                if (!_roomGoMap.TryGetValue(conn.RoomB, out var goB)) { Debug.LogWarning($"[Interiors]   Ã¢â€ â€˜ RoomB missing from _roomGoMap (Prefab null?)"); continue; }
                // Sanity: the two rooms should actually share a grid edge along the
                // connection's socket. If they don't, stripping leaves a hole opening
                // into the void with no neighbor on the other side.
                if (!RoomsShareEdge(conn.RoomA, conn.RoomB, conn.SocketA))
                {
                    Debug.LogWarning($"[Interiors] Open-passage connection {conn.RoomA.Definition.name}@{conn.RoomA.GridPosition} <-> {conn.RoomB.Definition.name}@{conn.RoomB.GridPosition} via {conn.SocketA} doesn't share a grid edge; skipping wall strip to avoid abyss.");
                    continue;
                }
                // Wall/anchor geometry is in DEF-local frame inside the prefab, so we
                // need the def-socket that the world-socket maps back to.
                var defSA = conn.SocketA.IsVertical() ? conn.SocketA
                    : SocketDirectionExtensions.Rotate(conn.SocketA, -conn.RoomA.Rotation);
                var defSB = conn.SocketB.IsVertical() ? conn.SocketB
                    : SocketDirectionExtensions.Rotate(conn.SocketB, -conn.RoomB.Rotation);
                StripWallAtSocket(goA, defSA);
                StripWallAtSocket(goB, defSB);
                StripAnchorsAtWall(goA, defSA);
                StripAnchorsAtWall(goB, defSB);
                // If the two rooms share more than one cell along this side (e.g. a 2x1
                // dining room whose long side fully sits against a kitchen), strip the
                // ENTIRE wall on that side, not just the door cell. Removes the lintel +
                // Wall_Rest + corner segments so the two rooms read as one open space.
                bool fullStrip = CountSharedEdgeCells(conn.RoomA, conn.RoomB, conn.SocketA) > 1;
                // Tiled hallway corridors: strip the lintel between adjacent hallway tiles
                // so 1x1 fillers chained next to a 4x1 hallway read as one continuous
                // corridor instead of a rhythmic series of doorless arches.
                if (!fullStrip
                    && conn.RoomA.Definition.Kind == RoomKind.Hallway
                    && conn.RoomB.Definition.Kind == RoomKind.Hallway)
                {
                    fullStrip = true;
                }
                if (fullStrip)
                {
                    StripFullWallAtSocket(goA, defSA);
                    StripFullWallAtSocket(goB, defSB);
                }
            }

            return root;
        }

        // True if any of RoomA's occupied cells sits directly against any of RoomB's
        // occupied cells along the world-socket axis. Catches connections that survived
        // registration but ended up pointing at a misaligned neighbor (which would leave
        // a stripped wall opening into nothing).
        private static bool RoomsShareEdge(PlacedRoom a, PlacedRoom b, SocketDirection worldSocket)
        {
            if (worldSocket.IsVertical())
            {
                // Vertical: same XZ cell, adjacent Y.
                int dy = worldSocket == SocketDirection.Up ? 1 : -1;
                foreach (var ca in a.OccupiedCells())
                {
                    var below = new Vector3Int(ca.x, ca.y + dy, ca.z);
                    foreach (var cb in b.OccupiedCells())
                        if (cb == below) return true;
                }
                return false;
            }
            // Horizontal: cells on opposite sides of the shared edge.
            int dxA = 0, dzA = 0;
            switch (worldSocket)
            {
                case SocketDirection.North: dzA =  1; break;
                case SocketDirection.South: dzA = -1; break;
                case SocketDirection.East:  dxA =  1; break;
                case SocketDirection.West:  dxA = -1; break;
            }
            foreach (var ca in a.OccupiedCells())
            {
                var nb = new Vector3Int(ca.x + dxA, ca.y, ca.z + dzA);
                foreach (var cb in b.OccupiedCells())
                    if (cb == nb) return true;
            }
            return false;
        }

        // Remove FurnitureAnchors whose `Wall` matches the destroyed boundary Ã¢â‚¬â€ they'd
        // be hanging in empty air at the seam between two open-passage rooms.
        private static void StripAnchorsAtWall(GameObject roomRoot, SocketDirection wall)
        {
            foreach (var anchor in roomRoot.GetComponentsInChildren<FurnitureAnchor>())
                if (anchor.Wall == wall) Destroy(anchor.gameObject);
        }

        private static Shader _cachedPrimitiveShader;
        private static Shader GetPrimitiveShader()
        {
            if (_cachedPrimitiveShader != null) return _cachedPrimitiveShader;
            _cachedPrimitiveShader = Shader.Find("Universal Render Pipeline/Lit")
                                  ?? Shader.Find("Standard");
            return _cachedPrimitiveShader;
        }

        // Tinted-material cache. Keyed by 24-bit RGB so two primitives that ask for the
        // same colour share one Material instance Ã¢â‚¬â€ SRP batcher then groups their draws.
        // Static for the process lifetime; survives interior regen so we don't leak.
        private static readonly Dictionary<int, Material> _tintedMaterials = new();
        private static Material GetCachedTintedMaterial(Color tint)
        {
            int key = (Mathf.RoundToInt(tint.r * 255f) << 16)
                    | (Mathf.RoundToInt(tint.g * 255f) << 8)
                    |  Mathf.RoundToInt(tint.b * 255f);
            if (_tintedMaterials.TryGetValue(key, out var existing) && existing != null) return existing;
            var mat = new Material(GetPrimitiveShader()) { color = tint };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            _tintedMaterials[key] = mat;
            return mat;
        }

        private static void ShuffleInPlace<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Removes the door-cell frame pieces (and any runtime wall-patch) on the given side
        // of a room so the room opens fully onto its neighbour AT THE SHARED door cell only.
        // Wall_{s}_Rest is left intact Ã¢â‚¬â€ for multi-cell rooms it's the portion of the wall
        // that doesn't touch the neighbour and would otherwise expose the room to the void.
        // Number of cells along `worldSocket`'s side of A that are immediately adjacent
        // to a cell of B. Used to detect "full long-side" shared walls (e.g. dining
        // long side fully against kitchen) so we can strip the entire wall rather than
        // leaving a lintel + Wall_Rest mid-room.
        private static int CountSharedEdgeCells(PlacedRoom a, PlacedRoom b, SocketDirection worldSocket)
        {
            if (worldSocket.IsVertical()) return 0;
            int dx = 0, dz = 0;
            switch (worldSocket)
            {
                case SocketDirection.North: dz =  1; break;
                case SocketDirection.South: dz = -1; break;
                case SocketDirection.East:  dx =  1; break;
                case SocketDirection.West:  dx = -1; break;
            }
            var bCells = new HashSet<Vector3Int>();
            foreach (var cb in b.OccupiedCells()) bCells.Add(cb);
            int n = 0;
            foreach (var ca in a.OccupiedCells())
            {
                var nb = new Vector3Int(ca.x + dx, ca.y, ca.z + dz);
                if (bCells.Contains(nb)) n++;
            }
            return n;
        }

        // Destroys EVERY wall segment on the given def-side: door-frame lintel, Wall_Rest,
        // the solid wall (when no socket exists). Used after StripWallAtSocket when the
        // shared edge spans more than one cell.
        private static void StripFullWallAtSocket(GameObject roomRoot, SocketDirection s)
        {
            foreach (var name in new[] { $"Frame_{s}_T", $"Wall{s}_Rest", $"Wall{s}" })
            {
                var child = roomRoot.transform.Find(name);
                if (child != null) Destroy(child.gameObject);
            }
        }

        // For open-passage connections we used to delete the door frame outright, which
        // produced void-looking holes whenever the wall was a single cell wide (no
        // Wall_Rest segment to act as a backstop). Now we widen the door opening but
        // keep a thin top lintel so the archway always has a clear visual boundary,
        // and the player can't accidentally see all the way through unconnected
        // geometry past the neighbor.
        private static void StripWallAtSocket(GameObject roomRoot, SocketDirection s)
        {
            // The two pillars become unnecessary Ã¢â‚¬â€ the opening is the full door cell.
            // The wall patch (added for unconnected sockets) is also destroyed in case
            // it was somehow left around for a now-open passage.
            foreach (var name in new[] { $"Frame_{s}_L", $"Frame_{s}_R", $"WallPatch_{s}" })
            {
                var child = roomRoot.transform.Find(name);
                if (child != null) Destroy(child.gameObject);
            }
            // Keep Frame_{s}_T (the top lintel) so the opening always reads as a framed
            // archway. Without it, a stripped door-cell on a 1-cell wall leaves the room
            // completely walless on that side and visually opens to the void.
        }

        // Creates a tinted copy of the default room material so each building type reads
        // as visually distinct. Returns null when no tint is required (legacy / white).
        private static Material CreateThemedMaterial(BuildingDefinition def)
        {
            var color = def.ThemeColor;
            if (color == Color.white) return null;

            var src = GetDefaultRoomMaterial();
            if (src == null) return null;

            var mat = new Material(src) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.name = $"{def.DisplayName}_Themed";
            return mat;
        }

        private static Material _cachedDefaultRoomMaterial;
        private static Material GetDefaultRoomMaterial()
        {
            if (_cachedDefaultRoomMaterial != null) return _cachedDefaultRoomMaterial;
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cachedDefaultRoomMaterial = probe.GetComponent<MeshRenderer>().sharedMaterial;
            Destroy(probe);
            return _cachedDefaultRoomMaterial;
        }

        private static void ApplyThemedMaterial(GameObject roomRoot, Material themed)
        {
            foreach (var r in roomRoot.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = themed;
        }

        // World-space translation to apply AFTER rotation so the rotated room's SW
        // corner ends up at the same grid origin as the unrotated one. Rotation 0/2 are
        // sized-(s.x, s.y); rotation 1/3 are sized-(s.y, s.x) Ã¢â‚¬â€ translation matches.
        private static Vector3 RotationTranslation(PlacedRoom room, float cellMetres)
        {
            var s = room.Definition.GridSize;
            return room.Rotation switch
            {
                0 => Vector3.zero,
                1 => new Vector3(0f,            0f, s.x * cellMetres),
                2 => new Vector3(s.x * cellMetres, 0f, s.y * cellMetres),
                3 => new Vector3(s.y * cellMetres, 0f, 0f),
                _ => Vector3.zero,
            };
        }

        // Plugs the door-frame opening for any socket the generator left unconnected.
        // Door openings live on the SW-most cell of each wall (matching BuildPerimeterWall).
        // Vertical sockets (Up/Down) leave 2Ãƒâ€”2 holes in ceiling/floor at the NW corner Ã¢â‚¬â€
        // patch those too so the bottom/top of a stair stack doesn't open into the void.
        private static void PatchUnconnectedSockets(Transform roomRoot, PlacedRoom room, BuildingDefinition def)
        {
            const float doorWidth  = 1.7f;
            const float doorHeight = 3f;
            const float wall       = 0.2f;
            // Match BuildFloorOrCeiling: 2 m wide Ãƒâ€” 4 m deep hole whose north edge sits
            // at the top step (staircase_cells * cellMetres - 1 m). The stair room is 2
            // cells wide in the doubled grid, hence the multiplier. The hole is shifted
            // east by one cell so the bottom/top landings sit in the east cell, freeing
            // the west cell for doors that won't crowd the stairs.
            const float holeW      = 2f;
            const float holeD      = 4f;
            const int   stairCells = 2;
            float c                = def.GridCellMeters;
            float holeMinX         = c;
            float holeCenterX      = holeMinX + holeW * 0.5f;
            float holeNorthZ       = stairCells * c - 1f;
            float h = def.FloorHeightMeters;
            float w = room.Definition.GridSize.x * c;
            float d = room.Definition.GridSize.y * c;

            // If the room's Up socket is unconnected, the ramp leads to a patched ceiling
            // and is just a dead end Ã¢â‚¬â€ remove it so the player isn't confused.
            if (room.Definition.HasSocket(SocketDirection.Up) &&
                !room.ConnectedSockets.Contains(SocketDirection.Up))
            {
                var ramp = roomRoot.Find("Ramp");
                if (ramp != null) Object.Destroy(ramp.gameObject);
            }

            // Patches are positioned in the room's DEF-local frame (the GameObject is
            // rotated as a whole). So we iterate DEF sockets, but the connected-check
            // compares against WORLD sockets Ã¢â‚¬â€ convert via the room's rotation.
            foreach (var s in room.Definition.Sockets)
            {
                var worldS = s.IsVertical()
                    ? s
                    : SocketDirectionExtensions.Rotate(s, room.Rotation);
                if (room.ConnectedSockets.Contains(worldS)) continue;

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
                        // Cap the ceiling hole Ã¢â‚¬â€ sits between holeSouthZ and holeNorthZ.
                        localPos = new Vector3(holeCenterX, h, holeNorthZ - holeD * 0.5f);
                        size     = new Vector3(holeW, wall, holeD);
                        break;
                    case SocketDirection.Down:
                        // Cap the floor hole at the same XZ.
                        localPos = new Vector3(holeCenterX, 0f, holeNorthZ - holeD * 0.5f);
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

        // For a Garage room, replace the wall geometry on whichever side faces the void
        // (no neighbouring room) with a panelled garage-door visual. Decorative Ã¢â‚¬â€ doesn't
        // open and isn't a real connection. Side priority: South, West, East, North so
        // the garage door tends to land on the front-facing side of the building.
        private static void TryAddGarageDoor(Transform roomRoot, PlacedRoom room,
            BuildingDefinition def, InteriorLayout layout)
        {
            if (room.Definition.Kind != RoomKind.Garage) return;

            SocketDirection? voidSide = null;
            foreach (var s in new[] { SocketDirection.South, SocketDirection.West, SocketDirection.East, SocketDirection.North })
            {
                if (IsRoomSideVoidFacing(layout, room, s)) { voidSide = s; break; }
            }
            if (!voidSide.HasValue) return;

            var defSide = SocketDirectionExtensions.Rotate(voidSide.Value, -room.Rotation);

            // Destroy whatever was on this side (Frame_X, WallX, Wall_Rest, WallPatch).
            // Garage has all 4 sockets so the wall is door-frame + maybe Wall_Rest +
            // maybe WallPatch (if unconnected). Plus a possible WallX for socket-less defs.
            foreach (var name in new[]
            {
                $"Frame_{defSide}_L", $"Frame_{defSide}_R", $"Frame_{defSide}_T",
                $"Wall{defSide}_Rest", $"Wall{defSide}", $"WallPatch_{defSide}",
            })
            {
                var child = roomRoot.Find(name);
                if (child != null) Destroy(child.gameObject);
            }

            BuildGarageDoorPanel(roomRoot, defSide, room.Definition, def);
        }

        // True if every cell along `worldSide`'s outer edge of `room` is unoccupied Ã¢â‚¬â€
        // i.e. there's no neighbour, so this side of the room is on the building's
        // outer perimeter and faces the void.
        private static bool IsRoomSideVoidFacing(InteriorLayout layout, PlacedRoom room, SocketDirection worldSide)
        {
            var rs = room.RotatedGridSize;
            var p  = room.GridPosition;
            int dx = 0, dz = 0;
            switch (worldSide)
            {
                case SocketDirection.North: dz =  1; break;
                case SocketDirection.South: dz = -1; break;
                case SocketDirection.East:  dx =  1; break;
                case SocketDirection.West:  dx = -1; break;
                default: return false;
            }
            for (int x = 0; x < rs.x; x++)
            for (int z = 0; z < rs.y; z++)
            {
                bool onEdge = worldSide switch
                {
                    SocketDirection.North => z == rs.y - 1,
                    SocketDirection.South => z == 0,
                    SocketDirection.East  => x == rs.x - 1,
                    SocketDirection.West  => x == 0,
                    _ => false,
                };
                if (!onEdge) continue;
                var beyond = new Vector3Int(p.x + x + dx, p.y, p.z + z + dz);
                if (layout.IsCellOccupied(beyond)) return false;
            }
            return true;
        }

        // Builds a 4-panel faux garage door covering the full wall of `defSide`.
        // Geometry: a top trim above the door (fills to ceiling), then four horizontal
        // panels stacked from floor to door height with a small gap between each.
        private static void BuildGarageDoorPanel(Transform roomRoot, SocketDirection defSide,
            RoomDefinition roomDef, BuildingDefinition def)
        {
            float c          = def.GridCellMeters;
            float h          = def.FloorHeightMeters;
            const float doorH      = 3f;
            const float wallThick  = 0.2f;
            const int   panels     = 4;
            const float gap        = 0.04f;
            var panelColor = new Color(0.55f, 0.55f, 0.58f);
            var trimColor  = new Color(0.30f, 0.30f, 0.32f);

            float w = roomDef.GridSize.x * c;
            float d = roomDef.GridSize.y * c;
            bool ns = (defSide == SocketDirection.North || defSide == SocketDirection.South);
            float wallPos = ns
                ? (defSide == SocketDirection.North ? d : 0f)
                : (defSide == SocketDirection.East  ? w : 0f);
            float spanLen = ns ? w : d;
            float panelH  = doorH / panels;

            // Top trim from doorH up to the ceiling.
            if (h > doorH + 0.001f)
            {
                Vector3 tPos, tSize;
                if (ns)
                {
                    tPos  = new Vector3(spanLen * 0.5f, doorH + (h - doorH) * 0.5f, wallPos);
                    tSize = new Vector3(spanLen, h - doorH, wallThick);
                }
                else
                {
                    tPos  = new Vector3(wallPos, doorH + (h - doorH) * 0.5f, spanLen * 0.5f);
                    tSize = new Vector3(wallThick, h - doorH, spanLen);
                }
                AddPrimitiveCubeWithColor(roomRoot, $"GarageDoor_{defSide}_TopTrim", tPos, tSize, trimColor);
            }

            // Four horizontal panels. Slight side inset so panel ends don't bleed past walls.
            for (int i = 0; i < panels; i++)
            {
                float yCenter = (i + 0.5f) * panelH;
                float pH      = Mathf.Max(0.05f, panelH - gap);
                Vector3 pPos, pSize;
                if (ns)
                {
                    pPos  = new Vector3(spanLen * 0.5f, yCenter, wallPos);
                    pSize = new Vector3(spanLen - 0.1f, pH, wallThick);
                }
                else
                {
                    pPos  = new Vector3(wallPos, yCenter, spanLen * 0.5f);
                    pSize = new Vector3(wallThick, pH, spanLen - 0.1f);
                }
                AddPrimitiveCubeWithColor(roomRoot, $"GarageDoor_{defSide}_Panel{i}", pPos, pSize, panelColor);
            }
        }

        private static void AddPrimitiveCubeWithColor(Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = GetCachedTintedMaterial(color);
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
                if (conn.IsOpenPassage) continue;   // archways have no door
                var (pos, rot) = InteriorLayoutGenerator.DoorTransform(conn, origin, def);
                Debug.Log($"[Interiors] Door {conn.RoomA.Definition.name}@{conn.RoomA.GridPosition}r{conn.RoomA.Rotation}/{conn.SocketA} <-> {conn.RoomB.Definition.name}@{conn.RoomB.GridPosition}r{conn.RoomB.Rotation}/{conn.SocketB} at world {pos}");
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

        // Ã¢â€â‚¬Ã¢â€â‚¬ Utility Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

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

    }
}
