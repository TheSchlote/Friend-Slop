using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Furniture-placement passes split out of InteriorSceneBootstrapper to keep the main
    // file under the architecture-guardrail line ceiling. The placement pipeline runs in
    // phases per room:
    //   1. Satisfy each rule's minimum count from kind-restricted picks.
    //   2. Top up to the requested count from the general pool, respecting max caps.
    //   3. Tabletop pass — spawn small items on each placed piece's tabletop slots.
    //   4. Around-table pass — spawn chairs / surrounding pieces facing inward.
    //   5. Prune unused anchors (typical room sheds ~80% of authored anchor children).
    public partial class InteriorSceneBootstrapper
    {
        private const int TabletopMaxRolls = 4;
        // Tunables for anchor-time jitter applied just before SpawnFurnitureInstance.
        // Wall/Corner stay flush (no jitter); WallHanging gets a tiny triangular yaw nudge
        // that mostly reads as straight; Center gets a small XZ slide + small yaw rotation
        // so rugs/coffee tables don't read as grid-snapped.
        private const float WallHangingYawJitterDegrees = 4f;
        private const float CenterYawJitterDegrees = 10f;
        private const float CenterPosJitterMetres = 0.10f;
        private const float AroundTableJitterMetres = 0.10f;
        private const float AroundTableYawJitterDegrees = 20f;
        private const float FurnitureClearance = 0.1f;

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
    }
}
