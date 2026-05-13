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
        private readonly Dictionary<PlacedRoom, GameObject> _roomGoMap = new();
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
                // obstacles. Stripping the bake order: rooms → doors → furniture → bake.
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
                Debug.LogWarning("[Interior] Exit door not found in scene — was 'Repair Interior Scene' run?");
                yield break;
            }

            var def = ResolveDefinition();
            float halfCell = def != null ? def.GridCellMeters * 0.5f : 1.7f;
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

            // One tinted material instance, shared across every renderer in this building.
            Material themed = CreateThemedMaterial(def);

            // Reset the persistent room→GO map; SpawnFurniture reads it after rooms exist.
            _roomGoMap.Clear();

            // Sanity pass — any registered connection whose two rooms don't actually share
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
                Debug.LogWarning($"[Interiors] Connection {c.RoomA.Definition.name}@{c.RoomA.GridPosition} <-> {c.RoomB.Definition.name}@{c.RoomB.GridPosition} via {c.SocketA} doesn't share a grid edge — plugging the door cell instead of opening to the void.");
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
            // anchors against the stripped wall — they'd be floating in air.
            foreach (var conn in layout.Connections)
            {
                if (!conn.IsOpenPassage) continue;
                Debug.Log($"[Interiors] Archway {conn.RoomA.Definition.name}@{conn.RoomA.GridPosition}r{conn.RoomA.Rotation}/{conn.SocketA} <-> {conn.RoomB.Definition.name}@{conn.RoomB.GridPosition}r{conn.RoomB.Rotation}/{conn.SocketB}");
                if (!_roomGoMap.TryGetValue(conn.RoomA, out var goA)) { Debug.LogWarning($"[Interiors]   ↑ RoomA missing from _roomGoMap (Prefab null?)"); continue; }
                if (!_roomGoMap.TryGetValue(conn.RoomB, out var goB)) { Debug.LogWarning($"[Interiors]   ↑ RoomB missing from _roomGoMap (Prefab null?)"); continue; }
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

        // Remove FurnitureAnchors whose `Wall` matches the destroyed boundary — they'd
        // be hanging in empty air at the seam between two open-passage rooms.
        private static void StripAnchorsAtWall(GameObject roomRoot, SocketDirection wall)
        {
            foreach (var anchor in roomRoot.GetComponentsInChildren<FurnitureAnchor>())
                if (anchor.Wall == wall) Destroy(anchor.gameObject);
        }

        // ── Furniture placement ───────────────────────────────────────────────

        private void SpawnFurniture(InteriorLayout layout)
        {
            if (catalog == null) return;
            var furnitureCatalog = catalog.Furniture;
            if (furnitureCatalog == null || furnitureCatalog.Count == 0) return;

            // Build the per-room set of sockets that need door-cell clearance.
            // `room.ConnectedSockets` already contains every interior connection AND the
            // building's reserved exterior exit socket — both must be kept clear so a piece
            // doesn't spawn in the doorway. We skip Up/Down (no door swing on stairs).
            var doorsByRoom = new Dictionary<PlacedRoom, HashSet<SocketDirection>>();
            foreach (var room in layout.Rooms)
            {
                HashSet<SocketDirection> set = null;
                foreach (var s in room.ConnectedSockets)
                {
                    if (s.IsVertical()) continue;
                    if (set == null) set = new HashSet<SocketDirection>();
                    set.Add(s);
                }
                if (set != null) doorsByRoom[room] = set;
            }

            foreach (var room in layout.Rooms)
            {
                if (!_roomGoMap.TryGetValue(room, out var roomGo)) continue;
                SpawnFurnitureForRoom(room, roomGo,
                    doorsByRoom.TryGetValue(room, out var doors) ? doors : null,
                    furnitureCatalog);
            }
        }

        private void SpawnFurnitureForRoom(PlacedRoom room, GameObject roomGo,
            HashSet<SocketDirection> activeDoors, IReadOnlyList<FurnitureDefinition> catalogList)
        {
            var def     = room.Definition;
            var tags    = def.FurnitureTags;
            var range   = def.FurnitureCountRange;
            var rules   = def.FurnitureRules;
            if (range.y <= 0 && rules.Count == 0) return;
            float cellMetres = ResolveDefinition()?.GridCellMeters ?? 3.4f;

            // Per-room deterministic RNG.
            int roomSeed = unchecked(layout_Seed(_seed.Value)
                                     ^ (room.GridPosition.x * 73856093)
                                     ^ (room.GridPosition.y * 19349663)
                                     ^ (room.GridPosition.z * 83492791));
            var rng = new System.Random(roomSeed);

            // Collect surviving anchors (drop ones blocked by an active door cell).
            // a.Wall is stored in the prefab's DEF-local frame; activeDoors contains
            // WORLD sockets — convert via the room's rotation before comparing.
            var anchors = new List<FurnitureAnchor>(roomGo.GetComponentsInChildren<FurnitureAnchor>());
            for (int i = anchors.Count - 1; i >= 0; i--)
            {
                var a = anchors[i];
                if (a.Placement == AnchorPlacement.Center) continue;
                if (activeDoors == null) continue;
                var worldWall = a.Wall.IsVertical()
                    ? a.Wall
                    : SocketDirectionExtensions.Rotate(a.Wall, room.Rotation);
                if (activeDoors.Contains(worldWall) && AnchorIsOnDoorCell(a, room.Definition, a.Wall, cellMetres))
                    anchors.RemoveAt(i);
            }
            ShuffleInPlace(anchors, rng);

            int placed = 0;
            var placedByKind = new Dictionary<string, int>();
            var placedFootprints = new List<Rect>();   // world-XZ rects of pieces placed in this room
            var placedPieces    = new List<(FurnitureDefinition def, GameObject go)>(); // for tabletop pass
            // Track anchors that hosted a spawn so we can destroy the unused ones at the
            // end. Each room prefab carries dozens of anchors (Wall × cells × WallHanging
            // doubled + Corner + Center); typical rooms place 4–8 pieces — destroying the
            // remainder cuts GameObject count by ~80% and speeds up NavMesh bake.
            var usedAnchors = new HashSet<FurnitureAnchor>();

            // Reserve a swing zone in front of each active door so big Center pieces
            // (dining tables, console tables) can't park on top of a doorway. Wall/Corner
            // anchors are already filtered above; this protects the room interior too.
            if (activeDoors != null)
            {
                const float doorCellMetres = 3.4f;
                foreach (var worldS in activeDoors)
                {
                    var defS = worldS.IsVertical()
                        ? worldS
                        : SocketDirectionExtensions.Rotate(worldS, -room.Rotation);
                    var swing = DoorSwingFootprintWorld(defS, def, doorCellMetres, roomGo.transform);
                    if (swing.width > 0f && swing.height > 0f)
                        placedFootprints.Add(swing);
                }
            }

            // Phase 1: satisfy each rule's minimum count first. We try each anchor in
            // turn looking for a piece of the required kind that fits without colliding
            // with anything already placed.
            foreach (var rule in rules)
            {
                if (rule.Min <= 0) continue;
                for (int needed = rule.Min; needed > 0 && anchors.Count > 0; needed--)
                {
                    int hitIndex = -1;
                    FurnitureDefinition pick = null;
                    for (int i = 0; i < anchors.Count; i++)
                    {
                        var c = PickFurnitureOfKindForAnchor(catalogList, tags, anchors[i], rule.Kind, rng);
                        if (c == null) continue;
                        // Hanging items sit above floor furniture — they share XZ with what's
                        // below, but never the same height. Skip the floor-rect overlap test.
                        if (anchors[i].Placement != AnchorPlacement.WallHanging
                            && OverlapsExisting(c, anchors[i], placedFootprints)) continue;
                        pick = c; hitIndex = i; break;
                    }
                    if (pick == null) break; // none of our anchors can host this kind — skip
                    ApplyAnchorJitter(anchors[hitIndex], rng);
                    var pickedGo = SpawnFurnitureInstance(pick, anchors[hitIndex].transform, roomGo);
                    placedPieces.Add((pick, pickedGo));
                    usedAnchors.Add(anchors[hitIndex]);
                    if (anchors[hitIndex].Placement != AnchorPlacement.WallHanging)
                        placedFootprints.Add(WorldFootprint(pick, anchors[hitIndex]));
                    anchors.RemoveAt(hitIndex);
                    placedByKind.TryGetValue(rule.Kind, out var n);
                    placedByKind[rule.Kind] = n + 1;
                    placed++;
                }
            }

            // Phase 2: top up to the requested count from the general pool, respecting max caps.
            int minCount = Mathf.Max(0, range.x);
            int maxCount = Mathf.Max(minCount, range.y);
            int target = rng.Next(minCount, maxCount + 1);
            target = Mathf.Max(target, placed); // never undo required placements

            for (int i = 0; i < anchors.Count && placed < target; i++)
            {
                var anchor = anchors[i];
                var pick = PickFurnitureForAnchor(catalogList, tags, anchor, rng, rules, placedByKind);
                if (pick == null) continue;
                // WallHanging sits above floor furniture in Y — skip the floor-rect overlap test.
                if (anchor.Placement != AnchorPlacement.WallHanging
                    && OverlapsExisting(pick, anchor, placedFootprints)) continue;
                ApplyAnchorJitter(anchor, rng);
                var pickedGo = SpawnFurnitureInstance(pick, anchor.transform, roomGo);
                placedPieces.Add((pick, pickedGo));
                usedAnchors.Add(anchor);
                if (anchor.Placement != AnchorPlacement.WallHanging)
                    placedFootprints.Add(WorldFootprint(pick, anchor));
                placedByKind.TryGetValue(pick.Kind, out var n);
                placedByKind[pick.Kind] = n + 1;
                placed++;
            }

            // Phase 3: tabletop pass. Every placed piece that has tabletop slots (table,
            // counter, dresser, desk, etc.) spawns small themed items on top of it.
            SpawnTabletopFurniture(placedPieces, tags, catalogList, rng);

            // Phase 4: around-table pass. Each placed piece's AroundTableAnchors get a
            // chair (or whatever else matches AnchorPlacement.AroundTable + the room's
            // tags) facing inward toward the host piece.
            SpawnAroundTableFurniture(placedPieces, tags, catalogList, rng);

            // Phase 5: prune unused anchors. A typical residential room carries 40–60
            // FurnitureAnchor children (Wall + WallHanging × wall-cells + Corner + Center)
            // and only a handful host a piece. Destroying the rest cuts hierarchy
            // traversal cost for every subsequent operation (NavMesh bake, minimap, etc.)
            // and quiets the editor inspector. Tabletop/AroundTable transient anchors
            // are kept (they live under their host furniture).
            foreach (var a in roomGo.GetComponentsInChildren<FurnitureAnchor>())
            {
                if (a == null || usedAnchors.Contains(a)) continue;
                // Transient slots created by Phases 3/4 are siblings of their host; their
                // own transform has no children but their parent isn't the room root.
                if (a.Placement == AnchorPlacement.Tabletop || a.Placement == AnchorPlacement.AroundTable) continue;
                Object.Destroy(a.gameObject);
            }
        }

        // Walks each placed piece's tabletop anchors and fills them with small tabletop-
        // placement furniture that matches the room's furniture tags and fits the slot.
        // Each item gets a full 360° yaw and an XZ offset sized to the slack between its
        // own footprint and the slot's — never clips off the surface. If the rolled pose
        // overlaps an already-placed item on the same host, re-rolls up to TabletopMaxRolls
        // times; if still colliding, the slot is left empty rather than clipping.
        private const int TabletopMaxRolls = 4;
        private static void SpawnTabletopFurniture(
            List<(FurnitureDefinition def, GameObject go)> placedPieces,
            IReadOnlyList<string> roomTags,
            IReadOnlyList<FurnitureDefinition> catalogList,
            System.Random rng)
        {
            foreach (var (hostDef, hostGo) in placedPieces)
            {
                if (hostGo == null) continue;
                int idx = 0;
                var placedOnHost  = new List<Rect>();
                // Per-host de-dup sets so we don't end up with two of the same lamp / vase
                // / clock on a single table. Tracks both the def and its Kind tag — two
                // distinct lamp prefabs that share kind="table_lamp" still collide.
                var placedDefs    = new HashSet<FurnitureDefinition>();
                var placedKinds   = new HashSet<string>();
                foreach (var slot in hostDef.TabletopAnchors)
                {
                    // Create a transient FurnitureAnchor child so we can reuse the existing
                    // picker pipeline (which filters by placement + footprint + tags).
                    var anchorGo = new GameObject($"TabletopSlot_{idx++}");
                    anchorGo.transform.SetParent(hostGo.transform, false);
                    anchorGo.transform.localPosition = slot.localPosition;
                    var anchor = anchorGo.AddComponent<FurnitureAnchor>();
                    anchor.Configure(AnchorPlacement.Tabletop, SocketDirection.North, slot.footprintXZ);

                    var pick = PickFurnitureForAnchor(catalogList, roomTags, anchor, rng,
                        excludeDefs: placedDefs, excludeKinds: placedKinds);
                    if (pick == null) continue;

                    // Half the slack between the slot and item on each axis — keeps the
                    // jittered item entirely over the slot's surface footprint.
                    float slackX = Mathf.Max(0f, (slot.footprintXZ.x - pick.FootprintXZ.x) * 0.5f);
                    float slackZ = Mathf.Max(0f, (slot.footprintXZ.y - pick.FootprintXZ.y) * 0.5f);

                    bool placed = false;
                    for (int attempt = 0; attempt < TabletopMaxRolls && !placed; attempt++)
                    {
                        float dx  = ((float)rng.NextDouble() * 2f - 1f) * slackX;
                        float dz  = ((float)rng.NextDouble() * 2f - 1f) * slackZ;
                        float yaw = (float)rng.NextDouble() * 360f;
                        anchorGo.transform.localPosition    = slot.localPosition + new Vector3(dx, 0f, dz);
                        anchorGo.transform.localEulerAngles = new Vector3(0f, yaw, 0f);

                        var rect = WorldFootprint(pick, anchor);
                        bool overlaps = false;
                        for (int i = 0; i < placedOnHost.Count; i++)
                            if (placedOnHost[i].Overlaps(rect)) { overlaps = true; break; }
                        if (overlaps) continue;

                        placedOnHost.Add(rect);
                        placedDefs.Add(pick);
                        if (!string.IsNullOrEmpty(pick.Kind)) placedKinds.Add(pick.Kind);
                        SpawnFurnitureInstance(pick, anchor.transform, hostGo);
                        placed = true;
                    }
                }
            }
        }

        // Tunables for anchor-time jitter applied just before SpawnFurnitureInstance.
        // Wall/Corner stay flush (no jitter); WallHanging gets a tiny triangular yaw nudge
        // that mostly reads as straight; Center gets a small XZ slide + small yaw rotation
        // so rugs/coffee tables don't read as grid-snapped.
        private const float WallHangingYawJitterDegrees = 4f;
        private const float CenterYawJitterDegrees      = 10f;
        private const float CenterPosJitterMetres       = 0.10f;

        private static void ApplyAnchorJitter(FurnitureAnchor anchor, System.Random rng)
        {
            switch (anchor.Placement)
            {
                case AnchorPlacement.WallHanging:
                {
                    // Triangle distribution centred on 0 — most pieces near-level, occasional crooked.
                    float t   = (float)rng.NextDouble() - (float)rng.NextDouble();
                    var leul  = anchor.transform.localEulerAngles;
                    anchor.transform.localEulerAngles = new Vector3(leul.x, leul.y + t * WallHangingYawJitterDegrees, leul.z);
                    break;
                }
                case AnchorPlacement.Center:
                {
                    float dx  = ((float)rng.NextDouble() * 2f - 1f) * CenterPosJitterMetres;
                    float dz  = ((float)rng.NextDouble() * 2f - 1f) * CenterPosJitterMetres;
                    float yaw = ((float)rng.NextDouble() * 2f - 1f) * CenterYawJitterDegrees;
                    anchor.transform.localPosition += new Vector3(dx, 0f, dz);
                    var leul = anchor.transform.localEulerAngles;
                    anchor.transform.localEulerAngles = new Vector3(leul.x, leul.y + yaw, leul.z);
                    break;
                }
                // Wall and Corner: no jitter so floor pieces stay flush to walls / in corners.
            }
        }

        // Walks each placed piece's around-table anchors and spawns chairs (or other
        // AroundTable-tagged pieces) that match the room's tags. Each anchor carries its
        // own yaw so the chair faces the host piece from whichever side it sits on.
        // A small XZ offset and ±AroundTableYawJitterDegrees yaw jitter is applied to each
        // anchor so chairs don't line up perfectly — looks pushed in by a person, not
        // placed by a robot.
        private const float AroundTableJitterMetres      = 0.10f;
        private const float AroundTableYawJitterDegrees  = 20f;
        private static void SpawnAroundTableFurniture(
            List<(FurnitureDefinition def, GameObject go)> placedPieces,
            IReadOnlyList<string> roomTags,
            IReadOnlyList<FurnitureDefinition> catalogList,
            System.Random rng)
        {
            foreach (var (hostDef, hostGo) in placedPieces)
            {
                if (hostGo == null) continue;
                int idx = 0;
                foreach (var slot in hostDef.AroundTableAnchors)
                {
                    var anchorGo = new GameObject($"AroundTableSlot_{idx++}");
                    anchorGo.transform.SetParent(hostGo.transform, false);
                    float dx   = ((float)rng.NextDouble() * 2f - 1f) * AroundTableJitterMetres;
                    float dz   = ((float)rng.NextDouble() * 2f - 1f) * AroundTableJitterMetres;
                    float dyaw = ((float)rng.NextDouble() * 2f - 1f) * AroundTableYawJitterDegrees;
                    anchorGo.transform.localPosition    = slot.localPosition + new Vector3(dx, 0f, dz);
                    anchorGo.transform.localEulerAngles = new Vector3(0f, slot.yawDegrees + dyaw, 0f);
                    var anchor = anchorGo.AddComponent<FurnitureAnchor>();
                    anchor.Configure(AnchorPlacement.AroundTable, SocketDirection.North, slot.footprintXZ);

                    var pick = PickFurnitureForAnchor(catalogList, roomTags, anchor, rng);
                    if (pick == null) continue;
                    SpawnFurnitureInstance(pick, anchor.transform, hostGo);
                }
            }
        }

        // World-space XZ rectangle covered by `piece` when placed at `anchor`. The piece's
        // footprint is in its local XZ; we rotate the X/Z extents by the anchor's Y-rotation,
        // then translate to the anchor's world position. Includes a small clearance buffer.
        private const float FurnitureClearance = 0.1f;
        private static Rect WorldFootprint(FurnitureDefinition def, FurnitureAnchor anchor)
        {
            var fp = def.FootprintXZ;
            float hx = fp.x * 0.5f + FurnitureClearance;
            float hz = fp.y * 0.5f + FurnitureClearance;
            // Rotate the half-extents by the anchor's world yaw to get an AABB.
            float yaw = anchor.transform.eulerAngles.y * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(yaw));
            float s = Mathf.Abs(Mathf.Sin(yaw));
            float halfX = hx * c + hz * s;
            float halfZ = hx * s + hz * c;
            var p = anchor.transform.position;
            return new Rect(p.x - halfX, p.z - halfZ, halfX * 2f, halfZ * 2f);
        }

        private static bool OverlapsExisting(FurnitureDefinition def, FurnitureAnchor anchor,
            List<Rect> existing)
        {
            var r = WorldFootprint(def, anchor);
            for (int i = 0; i < existing.Count; i++)
                if (r.Overlaps(existing[i])) return true;
            return false;
        }

        // Like PickFurnitureForAnchor but restricted to a specific kind. Used by Phase 1
        // to satisfy minimum-count rules.
        private static FurnitureDefinition PickFurnitureOfKindForAnchor(
            IReadOnlyList<FurnitureDefinition> catalogList,
            IReadOnlyList<string> roomTags,
            FurnitureAnchor anchor,
            string kind,
            System.Random rng)
        {
            var candidates = new List<FurnitureDefinition>();
            foreach (var f in catalogList)
            {
                if (f == null) continue;
                if (f.Kind != kind) continue;
                if (f.Placement != anchor.Placement) continue;
                if (f.FootprintXZ.x > anchor.FootprintXZ.x + 0.01f) continue;
                if (f.FootprintXZ.y > anchor.FootprintXZ.y + 0.01f) continue;
                if (!HasTagOverlap(f.Tags, roomTags)) continue;
                for (int w = 0; w < f.Weight; w++) candidates.Add(f);
            }
            if (candidates.Count == 0) return null;
            return candidates[rng.Next(candidates.Count)];
        }

        private static int layout_Seed(int seed) => seed == 0 ? 1 : seed;

        // Anchor is on the door-cell if the anchor's local position falls within the
        // SW-most cell of the wall (where BuildPerimeterWall draws the doorway).
        private static bool AnchorIsOnDoorCell(FurnitureAnchor a, RoomDefinition roomDef, SocketDirection wall, float cellMetres)
        {
            var localPos = a.transform.localPosition;
            // Door cell width — read from BuildingDefinition.GridCellMeters at the call
            // site so non-default cell sizes work correctly.
            float c = cellMetres;
            switch (wall)
            {
                case SocketDirection.North:
                case SocketDirection.South:
                    return localPos.x <= c + 0.001f;       // door-cell is x=0..c
                case SocketDirection.East:
                case SocketDirection.West:
                    return localPos.z <= c + 0.001f;       // door-cell is z=0..c
            }
            return false;
        }

        // World-XZ rect covering the swing arc in front of a door. Door cells are SW-most
        // along their wall; the swing extends `swingDepth` metres into the room. Returned
        // as a world-aligned AABB by transforming the local corners through the room's
        // (rotated, translated) transform.
        private static Rect DoorSwingFootprintWorld(SocketDirection defSocket,
            RoomDefinition roomDef, float cellMetres, Transform roomTr)
        {
            const float swingDepth = 1.5f;
            float w = roomDef.GridSize.x * cellMetres;
            float d = roomDef.GridSize.y * cellMetres;
            float minX, maxX, minZ, maxZ;
            switch (defSocket)
            {
                case SocketDirection.North:
                    minX = 0f; maxX = cellMetres;
                    minZ = Mathf.Max(0f, d - swingDepth); maxZ = d;
                    break;
                case SocketDirection.South:
                    minX = 0f; maxX = cellMetres;
                    minZ = 0f; maxZ = Mathf.Min(d, swingDepth);
                    break;
                case SocketDirection.East:
                    minX = Mathf.Max(0f, w - swingDepth); maxX = w;
                    minZ = 0f; maxZ = cellMetres;
                    break;
                case SocketDirection.West:
                    minX = 0f; maxX = Mathf.Min(w, swingDepth);
                    minZ = 0f; maxZ = cellMetres;
                    break;
                default: return new Rect(0f, 0f, 0f, 0f);
            }
            var a = roomTr.TransformPoint(new Vector3(minX, 0f, minZ));
            var b = roomTr.TransformPoint(new Vector3(maxX, 0f, maxZ));
            float xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
            float zMin = Mathf.Min(a.z, b.z), zMax = Mathf.Max(a.z, b.z);
            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        private static FurnitureDefinition PickFurnitureForAnchor(
            IReadOnlyList<FurnitureDefinition> catalogList,
            IReadOnlyList<string> roomTags,
            FurnitureAnchor anchor,
            System.Random rng,
            IReadOnlyList<FurnitureRule> rules = null,
            Dictionary<string, int> placedByKind = null,
            HashSet<FurnitureDefinition> excludeDefs = null,
            HashSet<string> excludeKinds = null)
        {
            var candidates = new List<FurnitureDefinition>();
            foreach (var f in catalogList)
            {
                if (f == null) continue;
                if (f.Placement != anchor.Placement) continue;
                if (f.FootprintXZ.x > anchor.FootprintXZ.x + 0.01f) continue;
                if (f.FootprintXZ.y > anchor.FootprintXZ.y + 0.01f) continue;
                if (!HasTagOverlap(f.Tags, roomTags)) continue;
                if (IsCappedOut(f.Kind, rules, placedByKind)) continue;
                if (excludeDefs != null && excludeDefs.Contains(f)) continue;
                if (excludeKinds != null && !string.IsNullOrEmpty(f.Kind) && excludeKinds.Contains(f.Kind)) continue;

                for (int w = 0; w < f.Weight; w++) candidates.Add(f);
            }
            if (candidates.Count == 0) return null;
            return candidates[rng.Next(candidates.Count)];
        }

        private static bool HasTagOverlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a == null || b == null) return false;
            foreach (var t in a)
                for (int i = 0; i < b.Count; i++)
                    if (b[i] == t) return true;
            return false;
        }

        // Returns true if a rule for `kind` exists with a max>0 and we've already placed
        // that many pieces.
        private static bool IsCappedOut(string kind,
            IReadOnlyList<FurnitureRule> rules,
            Dictionary<string, int> placedByKind)
        {
            if (string.IsNullOrEmpty(kind) || rules == null || placedByKind == null) return false;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r.Kind != kind || r.Max <= 0) continue;
                placedByKind.TryGetValue(kind, out var placed);
                return placed >= r.Max;
            }
            return false;
        }

        private static GameObject SpawnFurnitureInstance(FurnitureDefinition def, Transform anchor, GameObject parent)
        {
            GameObject go;
            if (def.Prefab != null)
            {
                go = Object.Instantiate(def.Prefab, anchor.position, anchor.rotation, parent.transform);
            }
            else
            {
                go = new GameObject(def.DisplayName ?? def.name);
                go.transform.SetParent(parent.transform, false);
                go.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                foreach (var p in def.Primitives)
                {
                    var piece = GameObject.CreatePrimitive(p.shape);
                    piece.transform.SetParent(go.transform, false);
                    piece.transform.localPosition    = p.localPosition;
                    piece.transform.localScale       = p.localScale;
                    piece.transform.localEulerAngles = p.localEulerAngles;
                    // Default primitive material is the Standard shader — magenta under
                    // URP. Swap to a cached URP/Lit material keyed by tint so the SRP
                    // batcher can group draws and we don't leak materials on regen.
                    var r = piece.GetComponent<MeshRenderer>();
                    if (r != null) r.sharedMaterial = GetCachedTintedMaterial(p.tint);
                }
            }
            go.name = $"Furniture_{def.DisplayName ?? def.name}";
            return go;
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
        // same colour share one Material instance — SRP batcher then groups their draws.
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
        // Wall_{s}_Rest is left intact — for multi-cell rooms it's the portion of the wall
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
            // The two pillars become unnecessary — the opening is the full door cell.
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
        // sized-(s.x, s.y); rotation 1/3 are sized-(s.y, s.x) — translation matches.
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
        // Vertical sockets (Up/Down) leave 2×2 holes in ceiling/floor at the NW corner —
        // patch those too so the bottom/top of a stair stack doesn't open into the void.
        private static void PatchUnconnectedSockets(Transform roomRoot, PlacedRoom room, BuildingDefinition def)
        {
            const float doorWidth  = 1.7f;
            const float doorHeight = 3f;
            const float wall       = 0.2f;
            // Match BuildFloorOrCeiling: 2 m wide × 4 m deep hole whose north edge sits
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
            // and is just a dead end — remove it so the player isn't confused.
            if (room.Definition.HasSocket(SocketDirection.Up) &&
                !room.ConnectedSockets.Contains(SocketDirection.Up))
            {
                var ramp = roomRoot.Find("Ramp");
                if (ramp != null) Object.Destroy(ramp.gameObject);
            }

            // Patches are positioned in the room's DEF-local frame (the GameObject is
            // rotated as a whole). So we iterate DEF sockets, but the connected-check
            // compares against WORLD sockets — convert via the room's rotation.
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
                        // Cap the ceiling hole — sits between holeSouthZ and holeNorthZ.
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
        // (no neighbouring room) with a panelled garage-door visual. Decorative — doesn't
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

        // True if every cell along `worldSide`'s outer edge of `room` is unoccupied —
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
