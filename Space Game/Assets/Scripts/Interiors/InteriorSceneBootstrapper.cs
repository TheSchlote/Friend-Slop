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
                var layout = InteriorLayoutGenerator.Generate(_definition, _seed.Value, SocketDirection.South);
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
                if (themed != null) ApplyThemedMaterial(go, themed);
                _roomGoMap[room] = go;
            }

            // Strip wall geometry for open-passage connections so the two rooms read as one
            // continuous space (no door, no wall, no door-frame pillars). Also remove any
            // anchors against the stripped wall — they'd be floating in air.
            foreach (var conn in layout.Connections)
            {
                if (!conn.IsOpenPassage) continue;
                if (!_roomGoMap.TryGetValue(conn.RoomA, out var goA)) continue;
                if (!_roomGoMap.TryGetValue(conn.RoomB, out var goB)) continue;
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
            }

            return root;
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
                if (activeDoors.Contains(worldWall) && AnchorIsOnDoorCell(a, room.Definition, a.Wall))
                    anchors.RemoveAt(i);
            }
            ShuffleInPlace(anchors, rng);

            int placed = 0;
            var placedByKind = new Dictionary<string, int>();
            var placedFootprints = new List<Rect>();   // world-XZ rects of pieces placed in this room
            var placedPieces    = new List<(FurnitureDefinition def, GameObject go)>(); // for tabletop pass

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
                        if (OverlapsExisting(c, anchors[i], placedFootprints)) continue;
                        pick = c; hitIndex = i; break;
                    }
                    if (pick == null) break; // none of our anchors can host this kind — skip
                    var pickedGo = SpawnFurnitureInstance(pick, anchors[hitIndex].transform, roomGo);
                    placedPieces.Add((pick, pickedGo));
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
                if (OverlapsExisting(pick, anchor, placedFootprints)) continue;
                var pickedGo = SpawnFurnitureInstance(pick, anchor.transform, roomGo);
                placedPieces.Add((pick, pickedGo));
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
        }

        // Walks each placed piece's tabletop anchors and fills them with small tabletop-
        // placement furniture that matches the room's furniture tags and fits the slot.
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
                foreach (var slot in hostDef.TabletopAnchors)
                {
                    // Create a transient FurnitureAnchor child so we can reuse the existing
                    // picker pipeline (which filters by placement + footprint + tags).
                    var anchorGo = new GameObject($"TabletopSlot_{idx++}");
                    anchorGo.transform.SetParent(hostGo.transform, false);
                    anchorGo.transform.localPosition = slot.localPosition;
                    var anchor = anchorGo.AddComponent<FurnitureAnchor>();
                    anchor.Configure(AnchorPlacement.Tabletop, SocketDirection.North, slot.footprintXZ);

                    var pick = PickFurnitureForAnchor(catalogList, roomTags, anchor, rng);
                    if (pick == null) continue;
                    SpawnFurnitureInstance(pick, anchor.transform, hostGo);
                }
            }
        }

        // Walks each placed piece's around-table anchors and spawns chairs (or other
        // AroundTable-tagged pieces) that match the room's tags. Each anchor carries its
        // own yaw so the chair faces the host piece from whichever side it sits on.
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
                    anchorGo.transform.localPosition    = slot.localPosition;
                    anchorGo.transform.localEulerAngles = new Vector3(0f, slot.yawDegrees, 0f);
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
        private static bool AnchorIsOnDoorCell(FurnitureAnchor a, RoomDefinition roomDef, SocketDirection wall)
        {
            var localPos = a.transform.localPosition;
            // Door cell width — must match BuildingDefinition.gridCellMeters and the
            // builder's CellMetres constant.
            float c = 3.4f;
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
            Dictionary<string, int> placedByKind = null)
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
                var shader = GetPrimitiveShader();
                foreach (var p in def.Primitives)
                {
                    var piece = GameObject.CreatePrimitive(p.shape);
                    piece.transform.SetParent(go.transform, false);
                    piece.transform.localPosition    = p.localPosition;
                    piece.transform.localScale       = p.localScale;
                    piece.transform.localEulerAngles = p.localEulerAngles;
                    // Default primitive material is the Standard shader — magenta under
                    // URP. Replace it with a fresh URP/Lit material.
                    var r = piece.GetComponent<MeshRenderer>();
                    if (r != null)
                    {
                        var mat = new Material(shader) { color = p.tint };
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", p.tint);
                        r.sharedMaterial = mat;
                    }
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
        private static void StripWallAtSocket(GameObject roomRoot, SocketDirection s)
        {
            string[] childrenToDestroy =
            {
                $"Frame_{s}_L",
                $"Frame_{s}_R",
                $"Frame_{s}_T",
                $"WallPatch_{s}",
            };
            foreach (var name in childrenToDestroy)
            {
                var child = roomRoot.transform.Find(name);
                if (child != null) Destroy(child.gameObject);
            }
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
