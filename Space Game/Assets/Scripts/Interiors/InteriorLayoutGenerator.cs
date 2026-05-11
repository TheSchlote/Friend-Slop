using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace FriendSlop.Interiors
{
    // Pure static generator — no MonoBehaviour, fully unit-testable.
    // All clients run this with the same seed to get the same layout.
    public static class InteriorLayoutGenerator
    {
        private const int MaxGenerationAttempts = 8;
        private const int MaxExpansionIterations = 200;

        // ── Public API ─────────────────────────────────────────────────────────

        public static InteriorLayout Generate(BuildingDefinition def, int seed,
            SocketDirection? reservedExitSocket = null)
        {
            for (int i = 0; i < MaxGenerationAttempts; i++)
            {
                var layout = TryGenerate(def, seed + i, reservedExitSocket);
                if (layout != null) return layout;
            }
            return GenerateFallback(def, seed);
        }

        // ── Generation core ────────────────────────────────────────────────────

        private static InteriorLayout TryGenerate(BuildingDefinition def, int seed,
            SocketDirection? reservedExitSocket)
        {
            var rng = new Random(seed);
            var layout = new InteriorLayout { Seed = seed };

            int totalRooms    = rng.Next(def.MinRooms, def.MaxRooms + 1);
            int floors        = rng.Next(def.MinFloors, def.MaxFloors + 1);
            int targetSpecial = rng.Next(def.MinSpecialRooms, def.MaxSpecialRooms + 1);
            layout.FloorCount = floors;

            // Entry sits on the middle floor when there are 3+ floors so rooms can grow
            // both upward (upper floors) and downward (basement). For buildings that
            // explicitly want a basement below (skipBasementExpansion=true), put the
            // entry on the topmost floor so 'down' actually goes to a basement. Otherwise
            // floor 0.
            int startFloor;
            if (def.SkipBasementExpansion && floors >= 2)
                startFloor = floors - 1;
            else if (floors >= 3)
                startFloor = floors / 2;
            else
                startFloor = 0;
            layout.EntryFloor = startFloor;

            var roomsPerFloor = DistributeRooms(totalRooms, floors, rng);
            var frontier      = new List<OpenSocket>();

            // Build the required-room quota map. The entry (RoomCategory.Entry) is taken
            // from this list — falling back to legacy RoomPool entry rooms if no entry is
            // declared in the recipe (back-compat for older BuildingDefinitions).
            var quotas = BuildRequiredQuotas(def);

            var entryDef = PickEntryFromQuotas(quotas, rng)
                        ?? PickByCategory(def.RoomPool, RoomCategory.Entry, rng);
            if (entryDef == null) return null;

            var entry = TryPlaceAt(layout, entryDef, new Vector3Int(0, startFloor, 0));
            if (entry == null) return null;

            // Reserve a socket on the entry for the exterior exit door.
            if (reservedExitSocket.HasValue && entryDef.HasSocket(reservedExitSocket.Value))
            {
                entry.ConnectedSockets.Add(reservedExitSocket.Value);
                layout.ExitRoom   = entry;
                layout.ExitSocket = reservedExitSocket.Value;
            }

            AddSockets(frontier, entry);

            // If the recipe lists another entry-eligible required room (e.g. a LivingRoom),
            // try to place it adjacent to the entry now and flag the connection as an open
            // passage so the player walks straight through from entry into that room.
            TryPlaceAdjacentEntryCandidate(layout, frontier, rng, entry, quotas, reservedExitSocket);

            // Resolve any required rooms with explicit adjacency hints (e.g. DiningRoom must
            // sit next to the Kitchen). Done before generic expansion so the layout actually
            // honours the relationship.
            ProcessAdjacencyConstraints(layout, def, frontier, rng, quotas, startFloor);

            // Expand the entry floor first.
            int placedOnFloor = 1;
            ExpandFloor(layout, def, frontier, rng, startFloor,
                roomsPerFloor[startFloor], ref placedOnFloor, targetSpecial, quotas);

            // Walk upward from the entry to the top floor.
            for (int floor = startFloor; floor + 1 < floors; floor++)
            {
                if (!PlaceVerticalLink(layout, def, frontier, rng, floor, +1)) return null;
                placedOnFloor = 1;
                ExpandFloor(layout, def, frontier, rng, floor + 1,
                    roomsPerFloor[floor + 1], ref placedOnFloor, targetSpecial, quotas);
            }

            // Walk downward from the entry to the basement (floor 0).
            for (int floor = startFloor; floor - 1 >= 0; floor--)
            {
                if (!PlaceVerticalLink(layout, def, frontier, rng, floor, -1)) return null;
                placedOnFloor = 1;
                // Process adjacency hints first so e.g. a Basement_2x2 can grab the stair-
                // mirror's free sockets before generic expansion eats them.
                ProcessAdjacencyConstraints(layout, def, frontier, rng, quotas, floor - 1);
                if (def.SkipBasementExpansion && floor - 1 == 0) continue;
                ExpandFloor(layout, def, frontier, rng, floor - 1,
                    roomsPerFloor[floor - 1], ref placedOnFloor, targetSpecial, quotas);
            }

            // Any unfulfilled required rooms means this seed is unviable — fail so the
            // outer loop retries with a different seed.
            if (HasUnmetQuotas(quotas)) return null;
            if (layout.CountByCategory(RoomCategory.Special) < def.MinSpecialRooms) return null;
            return layout.Rooms.Count > 0 ? layout : null;
        }

        // ── Recipe quota tracking ─────────────────────────────────────────────

        private static Dictionary<RoomDefinition, int> BuildRequiredQuotas(BuildingDefinition def)
        {
            var quotas = new Dictionary<RoomDefinition, int>();
            foreach (var req in def.RequiredRooms)
            {
                if (req.Definition == null) continue;
                quotas.TryGetValue(req.Definition, out var existing);
                quotas[req.Definition] = existing + req.Count;
            }
            return quotas;
        }

        private static RoomDefinition PickEntryFromQuotas(Dictionary<RoomDefinition, int> quotas, Random rng)
        {
            var matches = new List<RoomDefinition>();
            foreach (var kv in quotas)
                if (kv.Key != null && kv.Key.IsEntryCandidate && kv.Value > 0)
                    matches.Add(kv.Key);
            if (matches.Count == 0) return null;
            var chosen = matches[rng.Next(matches.Count)];
            quotas[chosen]--;

            // If the chosen entry is a substitute (non-Entry-category room flagged as
            // entry-eligible, e.g. a LivingRoom acting as the foyer), zero any leftover
            // Entry-category required quotas so the building doesn't ALSO spawn a small
            // dedicated entry room — the LivingRoom is doing that job.
            if (chosen.Category != RoomCategory.Entry)
            {
                var keys = new List<RoomDefinition>(quotas.Keys);
                foreach (var k in keys)
                    if (k != chosen && k.Category == RoomCategory.Entry)
                        quotas[k] = 0;
            }

            return chosen;
        }

        // After the entry is placed, looks for any OTHER entry-candidate room still in the
        // required quotas. If found, tries to place it on a free socket of the entry and
        // marks that connection as an open passage. Falls back silently if the candidate
        // can't fit — the room will then be placed normally during expansion (with a door).
        private static void TryPlaceAdjacentEntryCandidate(
            InteriorLayout layout, List<OpenSocket> frontier, Random rng,
            PlacedRoom entry, Dictionary<RoomDefinition, int> quotas,
            SocketDirection? reservedExitSocket)
        {
            RoomDefinition partner = null;
            foreach (var kv in quotas)
            {
                if (kv.Value <= 0 || kv.Key == null) continue;
                if (!kv.Key.IsEntryCandidate) continue;
                partner = kv.Key;
                break;
            }
            if (partner == null) return;

            // Try every free socket on the entry (skipping the one reserved for the exit door).
            var sockets = new List<SocketDirection>(entry.Definition.Sockets);
            Shuffle(sockets, rng);
            foreach (var s in sockets)
            {
                if (s.IsVertical()) continue;
                if (reservedExitSocket.HasValue && s == reservedExitSocket.Value) continue;
                if (entry.ConnectedSockets.Contains(s)) continue;
                if (!partner.HasSocket(s.Opposite())) continue;

                var pos = NeighborOrigin(entry, s, partner.GridSize);
                if (pos.y != entry.GridPosition.y) continue;
                var placed = TryPlaceAt(layout, partner, pos);
                if (placed == null) continue;

                RegisterConnection(layout, entry, s, placed, s.Opposite(), isOpenPassage: true);
                AddSockets(frontier, placed);
                quotas[partner]--;
                return;
            }
        }

        private static bool HasUnmetQuotas(Dictionary<RoomDefinition, int> quotas)
        {
            foreach (var kv in quotas)
                if (kv.Value > 0) return true;
            return false;
        }

        // Places required rooms with adjacency hints. For each required room whose
        // AdjacentToAny list is non-empty, ensures a preferred parent exists in the layout
        // (force-placing one if needed) and then drops the room on a free socket of that
        // parent. Rooms whose parents can't be placed on the requested floor are deferred
        // to the normal expansion pass.
        private static void ProcessAdjacencyConstraints(InteriorLayout layout,
            BuildingDefinition def, List<OpenSocket> frontier, Random rng,
            Dictionary<RoomDefinition, int> quotas, int floor)
        {
            foreach (var req in def.RequiredRooms)
            {
                if (req.Definition == null) continue;
                if (req.AdjacentToAny.Count == 0) continue;
                if (!quotas.TryGetValue(req.Definition, out var remaining) || remaining <= 0) continue;

                // Find an already-placed parent matching one of the AdjacentToAny entries.
                PlacedRoom parent = FindPlacedRoomOnFloor(layout, req.AdjacentToAny, floor);

                // Otherwise, try to force one of the parents into the layout via frontier.
                if (parent == null)
                {
                    foreach (var pDef in req.AdjacentToAny)
                    {
                        if (pDef == null) continue;
                        parent = ForcePlaceRoomAtFrontier(layout, frontier, rng, pDef, floor);
                        if (parent != null)
                        {
                            if (quotas.TryGetValue(pDef, out var pq) && pq > 0)
                                quotas[pDef] = pq - 1;
                            break;
                        }
                    }
                }
                if (parent == null) continue;

                var placed = TryPlaceRoomAdjacentTo(layout, frontier, rng, req.Definition, parent);
                if (placed != null)
                    quotas[req.Definition] = remaining - 1;
            }
        }

        private static PlacedRoom FindPlacedRoomOnFloor(InteriorLayout layout,
            IReadOnlyList<RoomDefinition> candidates, int floor)
        {
            foreach (var room in layout.Rooms)
            {
                if (room.GridPosition.y != floor) continue;
                foreach (var c in candidates)
                    if (c != null && room.Definition == c) return room;
            }
            return null;
        }

        // Eagerly places `roomDef` on the given floor by walking the frontier for a socket
        // that accepts it. Returns the placed room (or null if no socket works).
        private static PlacedRoom ForcePlaceRoomAtFrontier(InteriorLayout layout,
            List<OpenSocket> frontier, Random rng, RoomDefinition roomDef, int floor)
        {
            if (roomDef == null) return null;
            var floorFrontier = FloorHorizontalFrontier(frontier, floor);
            Shuffle(floorFrontier, rng);
            foreach (var open in floorFrontier)
            {
                var needed = open.Socket.Opposite();
                if (!roomDef.HasSocket(needed)) continue;
                var pos = NeighborOrigin(open.Room, open.Socket, roomDef.GridSize);
                if (pos.y != floor) continue;
                var placed = TryPlaceAt(layout, roomDef, pos);
                if (placed == null) continue;
                frontier.Remove(open);
                RegisterConnection(layout, open.Room, open.Socket, placed, needed);
                AddSockets(frontier, placed);
                return placed;
            }
            return null;
        }

        private static PlacedRoom TryPlaceRoomAdjacentTo(InteriorLayout layout,
            List<OpenSocket> frontier, Random rng, RoomDefinition roomDef, PlacedRoom parent)
        {
            if (roomDef == null || parent == null) return null;
            var sockets = new List<SocketDirection>(parent.Definition.Sockets);
            Shuffle(sockets, rng);
            foreach (var s in sockets)
            {
                if (s.IsVertical()) continue;
                if (parent.ConnectedSockets.Contains(s)) continue;
                var needed = s.Opposite();
                if (!roomDef.HasSocket(needed)) continue;

                var pos = NeighborOrigin(parent, s, roomDef.GridSize);
                if (pos.y != parent.GridPosition.y) continue;
                var placed = TryPlaceAt(layout, roomDef, pos);
                if (placed == null) continue;

                // Remove the matching open-socket entry from the frontier so it isn't reused.
                frontier.RemoveAll(os => os.Room == parent && os.Socket == s);
                RegisterConnection(layout, parent, s, placed, needed);
                AddSockets(frontier, placed);
                return placed;
            }
            return null;
        }

        private static void ExpandFloor(InteriorLayout layout, BuildingDefinition def,
            List<OpenSocket> frontier, Random rng, int floor, int target,
            ref int placedOnFloor, int targetSpecial,
            Dictionary<RoomDefinition, int> quotas)
        {
            int iters = 0;
            while (placedOnFloor < target && iters < MaxExpansionIterations)
            {
                iters++;
                var floorFrontier = FloorHorizontalFrontier(frontier, floor);
                if (floorFrontier.Count == 0) break;

                var open = floorFrontier[rng.Next(floorFrontier.Count)];
                frontier.Remove(open);

                bool wantSpecial = layout.CountByCategory(RoomCategory.Special) < targetSpecial;
                var placed = TryPlaceNeighbor(layout, def, open, floor, rng, wantSpecial, quotas);
                if (placed != null)
                {
                    AddSockets(frontier, placed);
                    placedOnFloor++;
                }
            }
        }

        // dir = +1 places a connector going UP (from `floor` to `floor+1`).
        // dir = -1 places a connector going DOWN (from `floor` to `floor-1`). The downward
        // case supports an asymmetric mirror (e.g. a 2×2 basement room) and a list of
        // preferred parent rooms that the connector should sit next to (e.g. Kitchen,
        // LivingRoom, Hallway).
        private static bool PlaceVerticalLink(InteriorLayout layout, BuildingDefinition def,
            List<OpenSocket> frontier, Random rng, int floor, int dir)
        {
            var connDef = PickVerticalConnector(def.RoomPool, rng);
            if (connDef == null) return false;

            var preferredParents = dir < 0 ? def.DownConnectorParents : null;
            var placed = ForceConnector(layout, def, frontier, connDef, floor, rng, preferredParents);
            if (placed == null) return false;

            // Pick the mirror prefab — defaults to the connector itself, but a building can
            // override the downward mirror (e.g. a 2×2 basement instead of another stair).
            var mirrorDef = (dir < 0 && def.DownwardConnectorMirror != null)
                ? def.DownwardConnectorMirror
                : connDef;

            // Mirror needs the matching vertical socket (Down for going up, Up for going
            // down) so the connection registers correctly.
            var requiredMirrorSocket = dir > 0 ? SocketDirection.Down : SocketDirection.Up;
            if (!mirrorDef.HasSocket(requiredMirrorSocket))
            {
                Debug.LogWarning($"[Interior] Mirror prefab '{mirrorDef.name}' lacks {requiredMirrorSocket} socket — falling back to the connector itself.");
                mirrorDef = connDef;
            }

            var mirrorPos = new Vector3Int(placed.GridPosition.x, floor + dir, placed.GridPosition.z);
            var mirror    = TryPlaceAt(layout, mirrorDef, mirrorPos);
            if (mirror == null) return false;

            // Going up: placed.Up ↔ mirror.Down.  Going down: placed.Down ↔ mirror.Up.
            if (dir > 0)
                RegisterConnection(layout, placed, SocketDirection.Up,   mirror, SocketDirection.Down);
            else
                RegisterConnection(layout, placed, SocketDirection.Down, mirror, SocketDirection.Up);

            AddSockets(frontier, mirror);
            return true;
        }

        // ── Placement helpers ──────────────────────────────────────────────────

        private static PlacedRoom TryPlaceNeighbor(
            InteriorLayout layout, BuildingDefinition def, OpenSocket open,
            int floor, Random rng, bool preferSpecial,
            Dictionary<RoomDefinition, int> quotas)
        {
            var needed = open.Socket.Opposite();

            // First, try to satisfy an unmet required-room quota with a room that fits this socket.
            var required = BuildRequiredCandidates(quotas, needed);
            if (required.Count > 0)
            {
                Shuffle(required, rng);
                foreach (var candidate in required)
                {
                    var room = TryPlaceCandidate(layout, open, needed, candidate);
                    if (room != null)
                    {
                        quotas[candidate]--;
                        return room;
                    }
                }
            }

            // Fall back to the optional pool. Required rooms aren't included automatically —
            // they're handled by the priority path above. If a required room has been zeroed
            // out (e.g. Entry_1x1 when LivingRoom became the entry), this prevents it from
            // showing up via the fallback. Floor info is passed so floor-restricted rooms
            // (e.g. ServerRoom top-floor-only) are excluded from invalid floors.
            var candidates = BuildCandidateList(def.OptionalPool, needed, vertical: false, preferSpecial,
                floor, layout.FloorCount, layout.EntryFloor);
            if (candidates.Count == 0 && preferSpecial)
                candidates = BuildCandidateList(def.OptionalPool, needed, vertical: false, preferSpecial: false,
                    floor, layout.FloorCount, layout.EntryFloor);
            if (candidates.Count == 0) return null;

            Shuffle(candidates, rng);

            foreach (var candidate in candidates)
            {
                var room = TryPlaceCandidate(layout, open, needed, candidate);
                if (room != null)
                {
                    // If this candidate also satisfies a required quota, decrement it.
                    if (quotas.TryGetValue(candidate, out var remaining) && remaining > 0)
                        quotas[candidate] = remaining - 1;
                    return room;
                }
            }
            return null;
        }

        // Shared placement attempt: stays on the current floor, places the room and
        // registers the socket connection.
        private static PlacedRoom TryPlaceCandidate(InteriorLayout layout, OpenSocket open,
            SocketDirection needed, RoomDefinition candidate)
        {
            var pos = NeighborOrigin(open.Room, open.Socket, candidate.GridSize);
            if (pos.y != open.Room.GridPosition.y) return null; // stay on current floor
            var room = TryPlaceAt(layout, candidate, pos);
            if (room == null) return null;
            RegisterConnection(layout, open.Room, open.Socket, room, needed);
            return room;
        }

        // Lists required rooms (with positive remaining count) whose socket set includes
        // `needed`, repeated by their remaining count so heavier-quota rooms are picked
        // proportionally more often.
        private static List<RoomDefinition> BuildRequiredCandidates(
            Dictionary<RoomDefinition, int> quotas, SocketDirection needed)
        {
            var list = new List<RoomDefinition>();
            foreach (var kv in quotas)
            {
                if (kv.Value <= 0 || kv.Key == null) continue;
                if (kv.Key.IsVerticalConnector) continue;       // vertical handled separately
                if (!kv.Key.HasSocket(needed)) continue;
                for (int i = 0; i < kv.Value; i++) list.Add(kv.Key);
            }
            return list;
        }

        private static PlacedRoom ForceConnector(
            InteriorLayout layout, BuildingDefinition def, List<OpenSocket> frontier,
            RoomDefinition connDef, int floor, Random rng,
            IReadOnlyList<RoomDefinition> preferredParents = null)
        {
            var allFrontier = FloorHorizontalFrontier(frontier, floor);
            // Try preferred-parent sockets first if any were provided.
            if (preferredParents != null && preferredParents.Count > 0)
            {
                var preferred = new List<OpenSocket>();
                foreach (var os in allFrontier)
                {
                    foreach (var p in preferredParents)
                        if (p != null && os.Room.Definition == p) { preferred.Add(os); break; }
                }
                var placed = TryConnectorAtSockets(layout, frontier, rng, connDef, preferred);
                if (placed != null) return placed;
                // Falls through to ANY frontier socket if preferred sockets don't pan out.
            }
            return TryConnectorAtSockets(layout, frontier, rng, connDef, allFrontier);
        }

        private static PlacedRoom TryConnectorAtSockets(InteriorLayout layout,
            List<OpenSocket> frontier, Random rng, RoomDefinition connDef, List<OpenSocket> candidates)
        {
            Shuffle(candidates, rng);
            foreach (var open in candidates)
            {
                if (!connDef.HasSocket(open.Socket.Opposite())) continue;
                var pos  = NeighborOrigin(open.Room, open.Socket, connDef.GridSize);
                var room = TryPlaceAt(layout, connDef, pos);
                if (room != null)
                {
                    frontier.Remove(open);
                    RegisterConnection(layout, open.Room, open.Socket, room, open.Socket.Opposite());
                    AddSockets(frontier, room);
                    return room;
                }
            }
            return null;
        }

        private static PlacedRoom TryPlaceAt(InteriorLayout layout, RoomDefinition def, Vector3Int pos)
        {
            var room = new PlacedRoom(def, pos);
            foreach (var cell in room.OccupiedCells())
                if (layout.IsCellOccupied(cell)) return null;

            foreach (var cell in room.OccupiedCells())
                layout.Grid[cell] = room;
            layout.Rooms.Add(room);
            return room;
        }

        private static void RegisterConnection(
            InteriorLayout layout,
            PlacedRoom a, SocketDirection sa,
            PlacedRoom b, SocketDirection sb,
            bool isOpenPassage = false)
        {
            a.ConnectedSockets.Add(sa);
            b.ConnectedSockets.Add(sb);
            layout.Connections.Add(new InteriorLayout.Connection(a, sa, b, sb, isOpenPassage));
        }

        // ── Position math ──────────────────────────────────────────────────────

        // Computes the grid origin of a neighbor room placed via the given socket.
        // For South/West the formula depends on the neighbor's size.
        public static Vector3Int NeighborOrigin(PlacedRoom from, SocketDirection socket, Vector2Int nbrSize)
        {
            var p = from.GridPosition;
            var s = from.Definition.GridSize;
            return socket switch
            {
                SocketDirection.North => new Vector3Int(p.x,           p.y,     p.z + s.y),
                SocketDirection.South => new Vector3Int(p.x,           p.y,     p.z - nbrSize.y),
                SocketDirection.East  => new Vector3Int(p.x + s.x,     p.y,     p.z),
                SocketDirection.West  => new Vector3Int(p.x - nbrSize.x, p.y,   p.z),
                SocketDirection.Up    => new Vector3Int(p.x,           p.y + 1, p.z),
                SocketDirection.Down  => new Vector3Int(p.x,           p.y - 1, p.z),
                _                    => throw new ArgumentOutOfRangeException(nameof(socket))
            };
        }

        // World-space position of the pivot (south-west-floor corner) of a placed room.
        public static Vector3 RoomWorldPosition(PlacedRoom room, Vector3 buildingOrigin, BuildingDefinition def)
        {
            var g = room.GridPosition;
            return buildingOrigin + new Vector3(
                g.x * def.GridCellMeters,
                g.y * def.FloorHeightMeters,
                g.z * def.GridCellMeters);
        }

        // Door prefab is hinge-pivoted: pivot sits at the bottom-left corner of the door,
        // mesh extends +X by doorWidth and +Y by doorHeight. So we return the hinge position
        // (offset back along the door's local +X by half the door width) and the door's
        // outward facing rotation. Y is at floor level so the door sits on the floor.
        private const float DoorWidth = 2f;

        public static (Vector3 position, Quaternion rotation) DoorTransform(
            InteriorLayout.Connection conn, Vector3 buildingOrigin, BuildingDefinition def)
        {
            var a = conn.RoomA;
            var cell = def.GridCellMeters;
            var floorH = def.FloorHeightMeters;
            var origin = buildingOrigin;
            var p = a.GridPosition;
            var s = a.Definition.GridSize;
            float floorY = origin.y + p.y * floorH;

            Vector3 openingCentre;
            Quaternion rot;
            // Door is on the SW-most cell of each wall (cellX=0 for N/S, cellZ=0 for E/W),
            // so multi-cell rooms still align with neighbouring 1×1 rooms on the grid.
            switch (conn.SocketA)
            {
                case SocketDirection.North:
                    openingCentre = new Vector3(origin.x + (p.x + 0.5f) * cell, floorY, origin.z + (p.z + s.y) * cell);
                    rot = Quaternion.Euler(0, 0, 0);
                    break;
                case SocketDirection.South:
                    openingCentre = new Vector3(origin.x + (p.x + 0.5f) * cell, floorY, origin.z + p.z * cell);
                    rot = Quaternion.Euler(0, 180, 0);
                    break;
                case SocketDirection.East:
                    openingCentre = new Vector3(origin.x + (p.x + s.x) * cell, floorY, origin.z + (p.z + 0.5f) * cell);
                    rot = Quaternion.Euler(0, 90, 0);
                    break;
                case SocketDirection.West:
                    openingCentre = new Vector3(origin.x + p.x * cell, floorY, origin.z + (p.z + 0.5f) * cell);
                    rot = Quaternion.Euler(0, 270, 0);
                    break;
                default: // Up/Down — centre of vertical connector room
                    return (
                        new Vector3(origin.x + (p.x + s.x * 0.5f) * cell, floorY + floorH * 0.5f, origin.z + (p.z + s.y * 0.5f) * cell),
                        Quaternion.identity);
            }

            // Shift back along the door's local +X by half the door width so the hinge
            // lands at one edge of the opening and the mesh fills the doorway.
            var hinge = openingCentre - rot * new Vector3(DoorWidth * 0.5f, 0f, 0f);
            return (hinge, rot);
        }

        // ── Frontier & selection helpers ───────────────────────────────────────

        private static void AddSockets(List<OpenSocket> frontier, PlacedRoom room)
        {
            foreach (var s in room.Definition.Sockets)
                if (!room.ConnectedSockets.Contains(s))
                    frontier.Add(new OpenSocket(room, s));
        }

        private static List<OpenSocket> FloorHorizontalFrontier(List<OpenSocket> frontier, int floor)
        {
            var result = new List<OpenSocket>();
            foreach (var os in frontier)
                if (os.Room.GridPosition.y == floor && !os.Socket.IsVertical())
                    result.Add(os);
            return result;
        }

        private static List<RoomDefinition> BuildCandidateList(
            System.Collections.Generic.IReadOnlyList<RoomDefinition> pool,
            SocketDirection needed, bool vertical, bool preferSpecial,
            int floor = -1, int floorCount = -1, int entryFloor = -1)
        {
            var list = new List<RoomDefinition>();
            foreach (var rd in pool)
            {
                if (rd.IsVerticalConnector != vertical) continue;
                if (!rd.HasSocket(needed)) continue;
                if (preferSpecial && rd.Category != RoomCategory.Special) continue;
                // Floor-restricted rooms are filtered out when caller knows the floor.
                if (floor >= 0 && !rd.AllowedOnFloor(floor, floorCount, entryFloor)) continue;
                for (int w = 0; w < rd.Weight; w++) list.Add(rd);
            }
            return list;
        }

        private static RoomDefinition PickByCategory(
            System.Collections.Generic.IReadOnlyList<RoomDefinition> pool,
            RoomCategory cat, Random rng)
        {
            var matches = new List<RoomDefinition>();
            foreach (var rd in pool)
                if (rd.Category == cat) matches.Add(rd);
            return matches.Count == 0 ? null : matches[rng.Next(matches.Count)];
        }

        private static RoomDefinition PickVerticalConnector(
            System.Collections.Generic.IReadOnlyList<RoomDefinition> pool, Random rng)
        {
            var matches = new List<RoomDefinition>();
            foreach (var rd in pool)
                if (rd.IsVerticalConnector) matches.Add(rd);
            return matches.Count == 0 ? null : matches[rng.Next(matches.Count)];
        }

        // ── Utility ────────────────────────────────────────────────────────────

        private static int[] DistributeRooms(int total, int floors, Random rng)
        {
            var result = new int[floors];
            int remaining = total;
            for (int i = 0; i < floors - 1; i++)
            {
                int share = Mathf.Max(1, remaining / (floors - i));
                int delta = rng.Next(-1, 2);
                result[i] = Mathf.Clamp(share + delta, 1, remaining - (floors - i - 1));
                remaining -= result[i];
            }
            result[floors - 1] = Mathf.Max(1, remaining);
            return result;
        }

        private static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static InteriorLayout GenerateFallback(BuildingDefinition def, int seed)
        {
            var layout = new InteriorLayout { Seed = seed, FloorCount = 1 };
            var entryDef = def.RoomPool.Count > 0 ? def.RoomPool[0] : null;
            if (entryDef != null) TryPlaceAt(layout, entryDef, Vector3Int.zero);
            return layout;
        }

        private readonly struct OpenSocket
        {
            public readonly PlacedRoom Room;
            public readonly SocketDirection Socket;
            public OpenSocket(PlacedRoom r, SocketDirection s) { Room = r; Socket = s; }
        }
    }
}
