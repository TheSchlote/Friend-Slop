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
<<<<<<< HEAD
        private const int MaxGenerationAttempts = 8;
        private const int MaxExpansionIterations = 200;
=======
        private const int MaxGenerationAttempts = 32;
        private const int MaxExpansionIterations = 400;
>>>>>>> origin/interiors-changes

        // ── Public API ─────────────────────────────────────────────────────────

        public static InteriorLayout Generate(BuildingDefinition def, int seed,
            SocketDirection? reservedExitSocket = null)
        {
            for (int i = 0; i < MaxGenerationAttempts; i++)
            {
                var layout = TryGenerate(def, seed + i, reservedExitSocket);
                if (layout != null) return layout;
            }
<<<<<<< HEAD
=======
            UnityEngine.Debug.LogWarning(
                $"[Interiors] {def?.name ?? "<null>"} exhausted {MaxGenerationAttempts} seeds (base={seed}); falling back to 1-room layout. Constraints may be too tight.");
>>>>>>> origin/interiors-changes
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

<<<<<<< HEAD
            // Entry sits on the middle floor when there are 3+ floors so rooms can grow
            // both upward (upper floors) and downward (basement). Otherwise floor 0.
            int startFloor = floors >= 3 ? floors / 2 : 0;
            layout.EntryFloor = startFloor;
=======
            // Where the entry lives. Buildings with a downward-connector mirror always
            // reserve floor 0 for the basement, so the entry goes on floor 1; any extra
            // floors stack above as upper floors. Buildings without a basement use the
            // legacy logic — entry on the middle floor (for 3+) or floor 0 otherwise.
            int startFloor;
            if (def.DownwardConnectorMirror != null && floors >= 2)
                startFloor = 1;
            else if (floors >= 3)
                startFloor = floors / 2;
            else
                startFloor = 0;
            layout.EntryFloor = startFloor;
            // Carry over the building-level overhang constraint so TryPlaceAt can reject
            // upper-floor placements that aren't supported by the floor below.
            layout.RestrictUpperFloorOverhang = def.RestrictUpperFloorOverhang;
            // Compute the target rectangle for forceRectangularLayout. Side ≈ sqrt(N×6)
            // where N=MaxRooms and 6 is a rough avg cells/room (mix of 1×1, 2×2, 4×4).
            // Width is centred on the entry's X (entry is at x=0); depth extends north.
            if (def.ForceRectangularLayout)
            {
                layout.RestrictToRectangle = true;
                // ×7 (was ×6) gives the facade rule + master-suite cluster room to fit
                // without pushing seed-failure rates up.
                int side = Mathf.CeilToInt(Mathf.Sqrt(def.MaxRooms * 7f));
                int half = side / 2;
                layout.RectMinXZ = new Vector2Int(-half,            0);
                layout.RectMaxXZ = new Vector2Int(side - 1 - half,  side - 1);
            }
            layout.RestrictFrontFacade = def.RestrictFrontFacade;
            // Carry over the building-level southern-edge constraint so TryPlaceAt can
            // reject any room that would land south of the entry.
            layout.RestrictSouthOfEntry = def.EntryAtSouthernEdge;
>>>>>>> origin/interiors-changes

            var roomsPerFloor = DistributeRooms(totalRooms, floors, rng);
            var frontier      = new List<OpenSocket>();

<<<<<<< HEAD
            var entryDef = PickByCategory(def.RoomPool, RoomCategory.Entry, rng);
=======
            // Build the required-room quota map. The entry (RoomCategory.Entry) is taken
            // from this list — falling back to legacy RoomPool entry rooms if no entry is
            // declared in the recipe (back-compat for older BuildingDefinitions).
            var quotas = BuildRequiredQuotas(def);

            var entryDef = PickEntryFromQuotas(quotas, rng)
                        ?? PickByCategory(def.RoomPool, RoomCategory.Entry, rng);
>>>>>>> origin/interiors-changes
            if (entryDef == null) return null;

            var entry = TryPlaceAt(layout, entryDef, new Vector3Int(0, startFloor, 0));
            if (entry == null) return null;

            // Reserve a socket on the entry for the exterior exit door.
            if (reservedExitSocket.HasValue && entryDef.HasSocket(reservedExitSocket.Value))
<<<<<<< HEAD
                entry.ConnectedSockets.Add(reservedExitSocket.Value);

            AddSockets(frontier, entry);

            // Expand the entry floor first.
            int placedOnFloor = 1;
            ExpandFloor(layout, def, frontier, rng, startFloor,
                roomsPerFloor[startFloor], ref placedOnFloor, targetSpecial);
=======
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
>>>>>>> origin/interiors-changes

            // Walk upward from the entry to the top floor.
            for (int floor = startFloor; floor + 1 < floors; floor++)
            {
                if (!PlaceVerticalLink(layout, def, frontier, rng, floor, +1)) return null;
                placedOnFloor = 1;
                ExpandFloor(layout, def, frontier, rng, floor + 1,
<<<<<<< HEAD
                    roomsPerFloor[floor + 1], ref placedOnFloor, targetSpecial);
=======
                    roomsPerFloor[floor + 1], ref placedOnFloor, targetSpecial, quotas);
>>>>>>> origin/interiors-changes
            }

            // Walk downward from the entry to the basement (floor 0).
            for (int floor = startFloor; floor - 1 >= 0; floor--)
            {
                if (!PlaceVerticalLink(layout, def, frontier, rng, floor, -1)) return null;
                placedOnFloor = 1;
<<<<<<< HEAD
                ExpandFloor(layout, def, frontier, rng, floor - 1,
                    roomsPerFloor[floor - 1], ref placedOnFloor, targetSpecial);
            }

            if (layout.CountByCategory(RoomCategory.Special) < def.MinSpecialRooms) return null;
            return layout.Rooms.Count > 0 ? layout : null;
=======
                // Process adjacency hints first so e.g. a Basement_2x2 can grab the stair-
                // mirror's free sockets before generic expansion eats them.
                ProcessAdjacencyConstraints(layout, def, frontier, rng, quotas, floor - 1);
                if (def.SkipBasementExpansion && floor - 1 == 0) continue;
                ExpandFloor(layout, def, frontier, rng, floor - 1,
                    roomsPerFloor[floor - 1], ref placedOnFloor, targetSpecial, quotas);
            }

            // Hallways read as plausible only when at least a few rooms door off them —
            // a one-connection hallway is just a stub. Try to top each one up to the target
            // before we run the viability checks below.
            TopUpHallwayConnections(layout, def, rng, quotas);

            // Filler pass — drop a 1x1 hallway in any empty cell that touches ≥2 rooms,
            // bridging orphan rooms and densifying corridors. Doesn't run if the building
            // doesn't define a fillerRoom.
            FillEmptyCellsWithHallway(layout, def);

            // Any unfulfilled required rooms means this seed is unviable — fail so the
            // outer loop retries with a different seed.
            if (HasUnmetQuotas(quotas)) return null;
            if (layout.CountByCategory(RoomCategory.Special) < def.MinSpecialRooms) return null;
            if (layout.Rooms.Count == 0) return null;
            if (!RoomsFaceTheVoid(layout)) return null;
            if (!PassesPublicPrivateLayout(layout, def)) return null;

            ApplyDoorPolicy(layout, def);
            return layout;
        }

        // Walks the door graph to verify two real-house properties:
        //   1. Every Bedroom is at depth ≥ 2 from the entry — i.e. no bedroom doors
        //      open straight off the entry / living room.
        //   2. On any floor with multiple bedrooms, all bedrooms reach via the same
        //      Hallway (a single shared "bedroom wing" hallway), not through different
        //      branches of the house.
        // Skipped entirely for buildings where the front-facade rule isn't enforced —
        // those buildings (office, factory) don't follow residential layout norms.
        private static bool PassesPublicPrivateLayout(InteriorLayout layout, BuildingDefinition def)
        {
            if (!def.RestrictFrontFacade) return true;

            var entry = FindEntryRoom(layout);
            if (entry == null) return true;  // no entry => no graph anchor, skip

            // BFS over door connections (skip vertical sockets — stairs are handled separately).
            var depth = new Dictionary<PlacedRoom, int> { [entry] = 0 };
            var queue = new Queue<PlacedRoom>();
            queue.Enqueue(entry);
            while (queue.Count > 0)
            {
                var room = queue.Dequeue();
                int d = depth[room];
                foreach (var c in layout.Connections)
                {
                    if (c.SocketA.IsVertical()) continue;
                    PlacedRoom other = null;
                    if (c.RoomA == room) other = c.RoomB;
                    else if (c.RoomB == room) other = c.RoomA;
                    if (other == null || depth.ContainsKey(other)) continue;
                    depth[other] = d + 1;
                    queue.Enqueue(other);
                }
            }

            // Rule 1: bedrooms at depth >= 2.
            foreach (var room in layout.Rooms)
            {
                if (!IsBedroom(room.Definition)) continue;
                if (!depth.TryGetValue(room, out var d) || d < 2) return false;
            }

            // Rule 2: per floor, bedrooms must share a hallway COMPONENT. A component is
            // a maximal connected set of hallway rooms (joined by hallway↔hallway doors).
            // Tiled 1x1 fillers chained to a 4x1 hallway form one component, so a wing
            // made of mixed sizes still satisfies "all bedrooms on the same wing".
            var hallwayComponent = BuildHallwayComponentMap(layout);

            var bedroomsByFloor = new Dictionary<int, List<PlacedRoom>>();
            foreach (var room in layout.Rooms)
            {
                if (!IsBedroom(room.Definition)) continue;
                int f = room.GridPosition.y;
                if (!bedroomsByFloor.TryGetValue(f, out var list))
                    bedroomsByFloor[f] = list = new List<PlacedRoom>();
                list.Add(room);
            }
            foreach (var kv in bedroomsByFloor)
            {
                if (kv.Value.Count < 2) continue;
                int sharedComponent = -1;
                foreach (var bedroom in kv.Value)
                {
                    int comp = -1;
                    foreach (var c in layout.Connections)
                    {
                        if (c.SocketA.IsVertical()) continue;
                        PlacedRoom other = null;
                        if (c.RoomA == bedroom) other = c.RoomB;
                        else if (c.RoomB == bedroom) other = c.RoomA;
                        if (other == null) continue;
                        if (IsHallway(other.Definition))
                        {
                            comp = hallwayComponent[other];
                            break;
                        }
                    }
                    if (comp < 0) return false;                       // bedroom not on any hallway
                    if (sharedComponent < 0) sharedComponent = comp;
                    else if (sharedComponent != comp) return false;   // bedrooms on different hallway wings
                }
            }
            return true;
        }

        // Assigns every Hallway room an integer component id based on hallway↔hallway
        // door connections. Two hallways with a direct door (or an open passage) share
        // an id; otherwise they're in separate components. Used to group tiled hallways
        // into a single logical wing.
        private static Dictionary<PlacedRoom, int> BuildHallwayComponentMap(InteriorLayout layout)
        {
            var map = new Dictionary<PlacedRoom, int>();
            int next = 0;
            foreach (var room in layout.Rooms)
            {
                if (!IsHallway(room.Definition) || map.ContainsKey(room)) continue;
                int compId = next++;
                var queue = new Queue<PlacedRoom>();
                queue.Enqueue(room);
                map[room] = compId;
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var c in layout.Connections)
                    {
                        if (c.SocketA.IsVertical()) continue;
                        PlacedRoom other = null;
                        if (c.RoomA == cur) other = c.RoomB;
                        else if (c.RoomB == cur) other = c.RoomA;
                        if (other == null || !IsHallway(other.Definition) || map.ContainsKey(other)) continue;
                        map[other] = compId;
                        queue.Enqueue(other);
                    }
                }
            }
            return map;
        }

        private static PlacedRoom FindEntryRoom(InteriorLayout layout)
        {
            foreach (var r in layout.Rooms)
                if (r.Definition.Kind == RoomKind.Entry || r.Definition.Category == RoomCategory.Entry)
                    return r;
            return null;
        }

        // Some rooms read as "real" only when at least one of their longest walls borders
        // empty space — the Kitchen wants a sink-under-the-window wall, the Garage wants a
        // wall for the garage door, the Sun Room wants a glass wall. This check rejects any
        // seed where a room of this type ends up landlocked.
        private static bool RoomsFaceTheVoid(InteriorLayout layout)
        {
            foreach (var room in layout.Rooms)
            {
                if (!NeedsVoidWall(room.Definition)) continue;
                if (!HasLongWallToVoid(layout, room)) return false;
            }
            return true;
        }

        private static bool NeedsVoidWall(RoomDefinition def) =>
            IsKitchen(def) || IsGarage(def) || IsSunRoom(def);

        // True when the room has at least one of its longest-length walls (the side(s)
        // perpendicular to the long axis) bordering all-empty cells. For a square room
        // every wall qualifies as longest. Uses the rotated grid size so rotated rooms
        // are evaluated correctly.
        private static bool HasLongWallToVoid(InteriorLayout layout, PlacedRoom room)
        {
            var p = room.GridPosition;
            var s = room.RotatedGridSize;
            int maxLength = Mathf.Max(s.x, s.y);

            if (s.x == maxLength)
            {
                if (RowEmpty(layout, p.x, p.x + s.x - 1, p.z + s.y, p.y)) return true;     // north wall
                if (RowEmpty(layout, p.x, p.x + s.x - 1, p.z - 1, p.y))   return true;     // south wall
            }
            if (s.y == maxLength)
            {
                if (ColumnEmpty(layout, p.x + s.x, p.z, p.z + s.y - 1, p.y)) return true;  // east wall
                if (ColumnEmpty(layout, p.x - 1,   p.z, p.z + s.y - 1, p.y)) return true;  // west wall
            }
            return false;
        }

        private static bool RowEmpty(InteriorLayout layout, int xStart, int xEnd, int z, int y)
        {
            for (int x = xStart; x <= xEnd; x++)
                if (layout.Grid.ContainsKey(new Vector3Int(x, y, z))) return false;
            return true;
        }

        private static bool ColumnEmpty(InteriorLayout layout, int x, int zStart, int zEnd, int y)
        {
            for (int z = zStart; z <= zEnd; z++)
                if (layout.Grid.ContainsKey(new Vector3Int(x, y, z))) return false;
            return true;
        }

        // For buildings with doorsOnlyForPrivateRooms set, walk the connection list and
        // convert any horizontal connection that doesn't involve a Bedroom/Bathroom/Stair/
        // Basement into an open archway (no door, no wall, no frame). Vertical sockets are
        // left alone — they're stairs/holes, never had doors anyway.
        //
        // Extra rule for hallways: only the SHORT ENDS of a hallway (the walls perpendicular
        // to its long axis) can become archways. The long sides of a hallway always keep
        // their doors so the corridor reads as a corridor with rooms doored off it, not as
        // a single open space.
        private static void ApplyDoorPolicy(InteriorLayout layout, BuildingDefinition def)
        {
            if (!def.DoorsOnlyForPrivateRooms) return;
            for (int i = 0; i < layout.Connections.Count; i++)
            {
                var c = layout.Connections[i];
                if (c.IsOpenPassage) continue;
                if (c.SocketA.IsVertical()) continue;
                if (IsPrivateRoom(c.RoomA.Definition) || IsPrivateRoom(c.RoomB.Definition)) continue;
                if (IsHallway(c.RoomA.Definition) && !IsHallwayShortEnd(c.RoomA.RotatedGridSize, c.SocketA)) continue;
                if (IsHallway(c.RoomB.Definition) && !IsHallwayShortEnd(c.RoomB.RotatedGridSize, c.SocketB)) continue;
                layout.Connections[i] = new InteriorLayout.Connection(
                    c.RoomA, c.SocketA, c.RoomB, c.SocketB, isOpenPassage: true);
            }
        }

        // True if `worldSocket` is on the short (1-cell-wide) end of a hallway with the
        // given ROTATED size. For a 1×N (narrow + deep) hallway, the short ends are N/S;
        // for an N×1 (wide + shallow) they're E/W. Square hallways have no distinction —
        // treat every side as short.
        private static bool IsHallwayShortEnd(Vector2Int rotatedSize, SocketDirection worldSocket)
        {
            var s = rotatedSize;
            if (s.x == s.y) return true;
            if (s.x < s.y)
                return worldSocket == SocketDirection.North || worldSocket == SocketDirection.South;
            return worldSocket == SocketDirection.East || worldSocket == SocketDirection.West;
        }

        // "Private" rooms keep their doors (and walls) under the doors-only-for-private-rooms
        // policy. Anything else becomes an open archway. The list covers every room that
        // would, in a real house, have an interior door.
        private static bool IsPrivateRoom(RoomDefinition def)
        {
            if (def == null || def.name == null) return false;
            var n = def.name;
            return n.Contains("Bedroom")        // includes MasterBedroom
                || n.Contains("Bathroom")       // includes MasterBathroom
                || n.Contains("Basement")
                || n.Contains("Stair")
                || n.Contains("Closet")         // WalkinCloset, LinenCloset
                || n.Contains("PowderRoom")
                || n.Contains("Pantry")
                || n.Contains("Laundry")
                || n.Contains("Office")
                || n.Contains("GameRoom")
                || n.Contains("WineCellar")
                || n.Contains("Workshop")
                || n.Contains("MechanicalRoom")
                || n.Contains("Garage");
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
            // Entry is placed at rotation 0; partner can rotate independently.
            var entryDefSockets = new List<SocketDirection>(entry.Definition.Sockets);
            Shuffle(entryDefSockets, rng);
            foreach (var defS in entryDefSockets)
            {
                if (defS.IsVertical()) continue;
                var worldS = SocketDirectionExtensions.Rotate(defS, entry.Rotation);
                if (reservedExitSocket.HasValue && worldS == reservedExitSocket.Value) continue;
                if (entry.ConnectedSockets.Contains(worldS)) continue;
                var needed = worldS.Opposite();

                foreach (var rot in ShuffledRotations(rng))
                {
                    var partnerDefSocket = SocketDirectionExtensions.Rotate(needed, -rot);
                    if (!partner.HasSocket(partnerDefSocket)) continue;
                    var pos = NeighborOrigin(entry, worldS, partner, rot);
                    if (pos.y != entry.GridPosition.y) continue;
                    var placed = TryPlaceAt(layout, partner, pos, rot);
                    if (placed == null) continue;

                    RegisterConnection(layout, entry, worldS, placed, needed, isOpenPassage: true);
                    AddSockets(frontier, placed);
                    quotas[partner]--;
                    return;
                }
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
                foreach (var rot in ShuffledRotations(rng))
                {
                    var defSocket = SocketDirectionExtensions.Rotate(needed, -rot);
                    if (!roomDef.HasSocket(defSocket)) continue;
                    var pos = NeighborOrigin(open.Room, open.Socket, roomDef, rot);
                    if (pos.y != floor) continue;
                    var placed = TryPlaceAt(layout, roomDef, pos, rot);
                    if (placed == null) continue;
                    frontier.Remove(open);
                    RegisterConnection(layout, open.Room, open.Socket, placed, needed);
                    AddSockets(frontier, placed);
                    return placed;
                }
            }
            return null;
        }

        private static PlacedRoom TryPlaceRoomAdjacentTo(InteriorLayout layout,
            List<OpenSocket> frontier, Random rng, RoomDefinition roomDef, PlacedRoom parent)
        {
            if (roomDef == null || parent == null) return null;
            // Iterate the parent's WORLD sockets (def-sockets rotated into parent's frame).
            var parentDefSockets = new List<SocketDirection>(parent.Definition.Sockets);
            Shuffle(parentDefSockets, rng);
            foreach (var defS in parentDefSockets)
            {
                if (defS.IsVertical()) continue;
                var worldS = SocketDirectionExtensions.Rotate(defS, parent.Rotation);
                if (parent.ConnectedSockets.Contains(worldS)) continue;
                var needed = worldS.Opposite();

                foreach (var rot in ShuffledRotations(rng))
                {
                    var nbrDefSocket = SocketDirectionExtensions.Rotate(needed, -rot);
                    if (!roomDef.HasSocket(nbrDefSocket)) continue;
                    var pos = NeighborOrigin(parent, worldS, roomDef, rot);
                    if (pos.y != parent.GridPosition.y) continue;
                    var placed = TryPlaceAt(layout, roomDef, pos, rot);
                    if (placed == null) continue;

                    // Remove the matching open-socket entry from the frontier so it isn't reused.
                    frontier.RemoveAll(os => os.Room == parent && os.Socket == worldS);
                    RegisterConnection(layout, parent, worldS, placed, needed);
                    AddSockets(frontier, placed);
                    return placed;
                }
            }
            return null;
>>>>>>> origin/interiors-changes
        }

        private static void ExpandFloor(InteriorLayout layout, BuildingDefinition def,
            List<OpenSocket> frontier, Random rng, int floor, int target,
<<<<<<< HEAD
            ref int placedOnFloor, int targetSpecial)
=======
            ref int placedOnFloor, int targetSpecial,
            Dictionary<RoomDefinition, int> quotas)
>>>>>>> origin/interiors-changes
        {
            int iters = 0;
            while (placedOnFloor < target && iters < MaxExpansionIterations)
            {
                iters++;
                var floorFrontier = FloorHorizontalFrontier(frontier, floor);
                if (floorFrontier.Count == 0) break;

<<<<<<< HEAD
                var open = floorFrontier[rng.Next(floorFrontier.Count)];
                frontier.Remove(open);

                bool wantSpecial = layout.CountByCategory(RoomCategory.Special) < targetSpecial;
                var placed = TryPlaceNeighbor(layout, def, open, floor, rng, wantSpecial);
=======
                var open = def.CompactLayout
                    ? PickCompactSocket(layout, floorFrontier, rng)
                    : floorFrontier[rng.Next(floorFrontier.Count)];
                frontier.Remove(open);

                bool wantSpecial = layout.CountByCategory(RoomCategory.Special) < targetSpecial;
                var placed = TryPlaceNeighbor(layout, def, open, floor, rng, wantSpecial, quotas);
>>>>>>> origin/interiors-changes
                if (placed != null)
                {
                    AddSockets(frontier, placed);
                    placedOnFloor++;
                }
            }
        }

<<<<<<< HEAD
        // dir = +1 places a connector going UP (from `floor` to `floor+1`).
        // dir = -1 places a connector going DOWN (from `floor` to `floor-1`).
=======
        // Pick an open socket weighted toward placements adjacent to multiple existing
        // rooms — produces compact rectangular / L-shaped floor plans instead of sprawling
        // ones. 70% pick the highest-scoring socket; 30% spread across the top few for variety.
        private static OpenSocket PickCompactSocket(InteriorLayout layout,
            List<OpenSocket> floorFrontier, Random rng)
        {
            if (floorFrontier.Count == 1) return floorFrontier[0];

            int bestScore = -1;
            for (int i = 0; i < floorFrontier.Count; i++)
            {
                int s = SocketCompactnessScore(layout, floorFrontier[i]);
                if (s > bestScore) bestScore = s;
            }

            // Collect all sockets at or near the best score; then a weighted random pick.
            var top = new List<OpenSocket>();
            var rest = new List<OpenSocket>();
            for (int i = 0; i < floorFrontier.Count; i++)
            {
                int s = SocketCompactnessScore(layout, floorFrontier[i]);
                if (s >= bestScore) top.Add(floorFrontier[i]);
                else rest.Add(floorFrontier[i]);
            }

            if (rng.NextDouble() < 0.7 || rest.Count == 0)
                return top[rng.Next(top.Count)];
            return rest[rng.Next(rest.Count)];
        }

        // Number of grid cells adjacent to the socket's target position that are already
        // occupied. Higher = the new room would fill an inside corner, producing a more
        // compact footprint.
        private static int SocketCompactnessScore(InteriorLayout layout, OpenSocket open)
        {
            var pos = NeighborOrigin(open.Room, open.Socket, Vector2Int.one);
            int score = 0;
            if (layout.Grid.ContainsKey(new Vector3Int(pos.x + 1, pos.y, pos.z))) score++;
            if (layout.Grid.ContainsKey(new Vector3Int(pos.x - 1, pos.y, pos.z))) score++;
            if (layout.Grid.ContainsKey(new Vector3Int(pos.x, pos.y, pos.z + 1))) score++;
            if (layout.Grid.ContainsKey(new Vector3Int(pos.x, pos.y, pos.z - 1))) score++;
            return score;
        }

        // dir = +1 places a connector going UP (from `floor` to `floor+1`).
        // dir = -1 places a connector going DOWN (from `floor` to `floor-1`). The downward
        // case supports an asymmetric mirror (e.g. a 2×2 basement room) and a list of
        // preferred parent rooms that the connector should sit next to (e.g. Kitchen,
        // LivingRoom, Hallway).
>>>>>>> origin/interiors-changes
        private static bool PlaceVerticalLink(InteriorLayout layout, BuildingDefinition def,
            List<OpenSocket> frontier, Random rng, int floor, int dir)
        {
            var connDef = PickVerticalConnector(def.RoomPool, rng);
            if (connDef == null) return false;

<<<<<<< HEAD
            var placed = ForceConnector(layout, def, frontier, connDef, floor, rng);
            if (placed == null) return false;

            var mirrorPos = new Vector3Int(placed.GridPosition.x, floor + dir, placed.GridPosition.z);
            var mirror    = TryPlaceAt(layout, connDef, mirrorPos);
=======
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
>>>>>>> origin/interiors-changes
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
<<<<<<< HEAD
            int floor, Random rng, bool preferSpecial)
        {
            var needed = open.Socket.Opposite();

            var candidates = BuildCandidateList(def.RoomPool, needed, vertical: false, preferSpecial);
            if (candidates.Count == 0 && preferSpecial)
                candidates = BuildCandidateList(def.RoomPool, needed, vertical: false, preferSpecial: false);
=======
            int floor, Random rng, bool preferSpecial,
            Dictionary<RoomDefinition, int> quotas)
        {
            var needed = open.Socket.Opposite();

            // Constraint: a hallway can only be placed once enough non-hallway rooms exist,
            // so small buildings don't feel like nothing but corridors.
            bool blockHallway = CountWhere(layout, r => !IsHallway(r)) < MinNonHallwayRoomsBeforeHallway;
            int bedroomCount  = CountWhere(layout, IsBedroom);
            // Only buildings with the doors-only-for-private-rooms policy enforce the
            // hallway long-side rule. Office and Factory have many rooms doored off their
            // hallway sides, so this restriction would over-tighten their layouts.
            bool enforceHallwaySides = def.DoorsOnlyForPrivateRooms;

            // First, try to satisfy an unmet required-room quota with a room that fits this socket.
            var required = BuildRequiredCandidates(quotas, needed, floor, layout.FloorCount, layout.EntryFloor);
            if (required.Count > 0)
            {
                Shuffle(required, rng);
                foreach (var candidate in required)
                {
                    if (!PassesPlacementConstraints(open.Room, open.Socket, candidate,
                        bedroomCount, blockHallway, enforceHallwaySides, layout)) continue;
                    var room = TryPlaceCandidate(layout, open, needed, candidate, rng, enforceHallwaySides);
                    if (room != null)
                    {
                        if (candidate.Kind == RoomKind.MasterBedroom &&
                            !PlaceMasterSuiteCompanions(layout, def, room, rng))
                        {
                            UndoConnection(layout, room, open.Room, open.Socket);
                            continue;
                        }
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
>>>>>>> origin/interiors-changes
            if (candidates.Count == 0) return null;

            Shuffle(candidates, rng);

            foreach (var candidate in candidates)
            {
<<<<<<< HEAD
                var pos = NeighborOrigin(open.Room, open.Socket, candidate.GridSize);
                if (pos.y != open.Room.GridPosition.y) continue; // stay on current floor
                var room = TryPlaceAt(layout, candidate, pos);
                if (room != null)
                {
                    RegisterConnection(layout, open.Room, open.Socket, room, needed);
=======
                if (!PassesPlacementConstraints(open.Room, open.Socket, candidate,
                    bedroomCount, blockHallway, enforceHallwaySides, layout)) continue;
                var room = TryPlaceCandidate(layout, open, needed, candidate, rng, enforceHallwaySides);
                if (room != null)
                {
                    if (candidate.Kind == RoomKind.MasterBedroom &&
                        !PlaceMasterSuiteCompanions(layout, def, room, rng))
                    {
                        UndoConnection(layout, room, open.Room, open.Socket);
                        continue;
                    }
                    // If this candidate also satisfies a required quota, decrement it.
                    if (quotas.TryGetValue(candidate, out var remaining) && remaining > 0)
                        quotas[candidate] = remaining - 1;
>>>>>>> origin/interiors-changes
                    return room;
                }
            }
            return null;
        }

<<<<<<< HEAD
        private static PlacedRoom ForceConnector(
            InteriorLayout layout, BuildingDefinition def, List<OpenSocket> frontier,
            RoomDefinition connDef, int floor, Random rng)
        {
            var floorFrontier = FloorHorizontalFrontier(frontier, floor);
            Shuffle(floorFrontier, rng);

            foreach (var open in floorFrontier)
            {
                if (!connDef.HasSocket(open.Socket.Opposite())) continue;
                var pos  = NeighborOrigin(open.Room, open.Socket, connDef.GridSize);
=======
        // After placing a MasterBedroom, immediately place its MasterBathroom and
        // WalkinCloset adjacent. Both are required (per user's master-suite-must-cluster
        // rule) — if either can't fit on a free socket of the master, we report failure
        // and the caller undoes the master bedroom placement so the seed can try a
        // different position.
        private static bool PlaceMasterSuiteCompanions(InteriorLayout layout, BuildingDefinition def,
            PlacedRoom master, Random rng)
        {
            var bathDef   = FindRoomByKind(def.OptionalPool, RoomKind.MasterBathroom);
            var closetDef = FindRoomByKind(def.OptionalPool, RoomKind.WalkinCloset);
            if (bathDef == null || closetDef == null) return false;

            var openSockets = new List<SocketDirection>();
            foreach (var defSocket in master.Definition.Sockets)
            {
                if (defSocket.IsVertical()) continue;
                var worldSocket = SocketDirectionExtensions.Rotate(defSocket, master.Rotation);
                if (!master.ConnectedSockets.Contains(worldSocket))
                    openSockets.Add(worldSocket);
            }
            Shuffle(openSockets, rng);

            var bath = TryPlaceCompanion(layout, master, openSockets, bathDef, rng);
            if (bath == null) return false;
            var closet = TryPlaceCompanion(layout, master, openSockets, closetDef, rng);
            if (closet == null)
            {
                UndoConnection(layout, bath, master, FindConnectingSocket(master, bath));
                return false;
            }
            return true;
        }

        private static PlacedRoom TryPlaceCompanion(InteriorLayout layout, PlacedRoom parent,
            List<SocketDirection> availableSockets, RoomDefinition companion, Random rng)
        {
            for (int i = 0; i < availableSockets.Count; i++)
            {
                var sock   = availableSockets[i];
                if (parent.ConnectedSockets.Contains(sock)) continue;
                var open   = new OpenSocket(parent, sock);
                var needed = sock.Opposite();
                var room   = TryPlaceCandidate(layout, open, needed, companion, rng,
                    enforceHallwaySides: false);
                if (room != null)
                {
                    availableSockets.RemoveAt(i);
                    return room;
                }
            }
            return null;
        }

        private static RoomDefinition FindRoomByKind(IReadOnlyList<RoomDefinition> pool, RoomKind kind)
        {
            foreach (var rd in pool)
                if (rd != null && rd.Kind == kind) return rd;
            return null;
        }

        // Look up which world-socket of `parent` connects it to `child` so we can pass
        // it to UndoConnection. Returns the first match (rooms only have one shared edge).
        private static SocketDirection FindConnectingSocket(PlacedRoom parent, PlacedRoom child)
        {
            foreach (var s in parent.ConnectedSockets)
            {
                if (s.IsVertical()) continue;
                int dx = 0, dz = 0;
                switch (s)
                {
                    case SocketDirection.North: dz =  1; break;
                    case SocketDirection.South: dz = -1; break;
                    case SocketDirection.East:  dx =  1; break;
                    case SocketDirection.West:  dx = -1; break;
                }
                foreach (var ca in parent.OccupiedCells())
                {
                    var nb = new Vector3Int(ca.x + dx, ca.y, ca.z + dz);
                    foreach (var cb in child.OccupiedCells())
                        if (cb == nb) return s;
                }
            }
            return SocketDirection.North;  // shouldn't happen
        }

        // Reverses RegisterConnection + TryPlaceAt for one connection edge. Removes the
        // child room from layout/grid, removes the parent's socket from ConnectedSockets,
        // and removes the matching layout.Connections entry.
        private static void UndoConnection(InteriorLayout layout, PlacedRoom child,
            PlacedRoom parent, SocketDirection parentSocket)
        {
            for (int i = layout.Connections.Count - 1; i >= 0; i--)
            {
                var c = layout.Connections[i];
                if ((c.RoomA == parent && c.RoomB == child) ||
                    (c.RoomA == child  && c.RoomB == parent))
                {
                    layout.Connections.RemoveAt(i);
                    break;
                }
            }
            parent.ConnectedSockets.Remove(parentSocket);
            UndoPlacement(layout, child);
        }

        // Minimum number of non-hallway rooms that must exist in the layout before a
        // hallway is allowed to spawn. Keeps tiny buildings from being mostly corridor.
        private const int MinNonHallwayRoomsBeforeHallway = 3;

        // Pre-rotation filter: rejects candidates that can't be placed regardless of
        // rotation. The rotation-dependent rule (candidate hallway long-side) is checked
        // inside TryPlaceCandidate once a rotation is picked.
        private static bool PassesPlacementConstraints(
            PlacedRoom parent, SocketDirection parentWorldSocket,
            RoomDefinition candidate, int bedroomCount, bool blockHallway,
            bool enforceHallwaySides, InteriorLayout layout = null)
        {
            if (candidate == null) return false;
            if (blockHallway && IsHallway(candidate)) return false;
            // Per-building cap on instances of this room (e.g. Garage = 1).
            if (layout != null && candidate.MaxCount > 0
                && CountWhere(layout, r => r == candidate) >= candidate.MaxCount) return false;
            if (!CanAdjacentlyConnect(parent.Definition, candidate, bedroomCount)) return false;

            // Parent-side hallway long-side rule — depends on parent's rotation, which we
            // already know.
            if (enforceHallwaySides
                && IsHallway(parent.Definition)
                && !IsHallwayShortEnd(parent.RotatedGridSize, parentWorldSocket)
                && !IsPrivateRoom(candidate))
                return false;

            return true;
        }

        // Adjacency rules that hold regardless of which side is the "parent":
        //   1. A bathroom only opens onto a bedroom, hallway, or living room.
        //      Master bathrooms are stricter — they only open onto a master bedroom.
        //   2. Two bedrooms never share a wall — there's always a corridor between them.
        //   3. With 2+ bedrooms in the layout, bedrooms are private — their other sockets
        //      only open onto a bathroom.
        //   4. Pantries only open onto a kitchen, mud rooms only off the entry, walk-in
        //      closets only off a bedroom (regular or master).
        // Most rules are symmetric (checked from both sides). Rule 3 is one-directional
        // (parent=bedroom): new bedrooms can still spawn off a hallway, but a bedroom
        // can't open onto anything else once there's more than one.
        private static bool CanAdjacentlyConnect(RoomDefinition parent, RoomDefinition child,
            int bedroomCount)
        {
            // Master bath is more restrictive than a regular bathroom and must be checked
            // first — IsBathroom would also match "MasterBathroom" and let regular
            // bathroom-neighbours through.
            if (IsMasterBathroom(parent) && !IsMasterBedroom(child)) return false;
            if (IsMasterBathroom(child)  && !IsMasterBedroom(parent)) return false;

            if (IsBathroom(parent) && !IsBathroomNeighbor(child)) return false;
            if (IsBathroom(child)  && !IsBathroomNeighbor(parent)) return false;

            if (IsBedroom(parent) && IsBedroom(child)) return false;

            if (bedroomCount >= 2 && IsBedroom(parent)
                && !IsBathroom(child) && !IsWalkinCloset(child)) return false;

            if (IsPantry(parent) && !IsKitchen(child)) return false;
            if (IsPantry(child)  && !IsKitchen(parent)) return false;

            // MudRoom is the "drop zone" — typical front-entry coat room OR back-of-house
            // garage-to-kitchen transition. Any of those three parents read as natural.
            if (IsMudRoom(parent) && !IsMudRoomNeighbor(child)) return false;
            if (IsMudRoom(child)  && !IsMudRoomNeighbor(parent)) return false;

            if (IsWalkinCloset(parent) && !IsBedroom(child)) return false;
            if (IsWalkinCloset(child)  && !IsBedroom(parent)) return false;

            // Stairs may only open horizontally onto public/transit rooms — keeps
            // bedrooms private (you walk down a hallway to reach the stairs, not
            // straight out of a bedroom).
            if (IsStair(parent) && !IsStairNeighbor(child)) return false;
            if (IsStair(child)  && !IsStairNeighbor(parent)) return false;

            return true;
        }

        private static bool IsStair(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.Stair;

        // Rooms a MudRoom may open onto. Front-entry mudroom (Entry) OR back-of-house
        // service-core variant (Garage/Kitchen drop-zone chain).
        private static bool IsMudRoomNeighbor(RoomDefinition def) =>
            def != null && (
                def.Kind == RoomKind.Entry  ||
                def.Kind == RoomKind.Garage ||
                def.Kind == RoomKind.Kitchen);

        // Rooms that may have cells at z=0 on the entry floor. The southern facade is
        // the building's "front" — only public-facing rooms read as a real front.
        private static bool CanTouchFrontFacade(RoomDefinition def) =>
            def != null && (
                def.Kind == RoomKind.Entry      ||
                def.Kind == RoomKind.LivingRoom ||
                def.Kind == RoomKind.Office     ||
                def.Kind == RoomKind.PowderRoom);

        // Rooms a Stair may open onto via a horizontal socket. Vertical (Up/Down) stair
        // links between floors bypass CanAdjacentlyConnect entirely so they're unaffected.
        private static bool IsStairNeighbor(RoomDefinition def) =>
            def != null && (
                def.Kind == RoomKind.Hallway    ||
                def.Kind == RoomKind.LivingRoom ||
                def.Kind == RoomKind.Entry      ||
                def.Kind == RoomKind.Den        ||
                def.Kind == RoomKind.Office     ||
                def.Kind == RoomKind.Kitchen);

        private static bool IsBathroomNeighbor(RoomDefinition def) =>
            IsBedroom(def) || IsHallway(def) || IsLivingRoom(def);

        private static bool IsHallway(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.Hallway;

        // Minimum doors a hallway should have. A single-connection hallway is functionally
        // just a stub — the post-pass tries to attach more rooms at its other open sockets
        // until it hits this count (or runs out of viable placements). 4 = every wall
        // socket of a 4×1 hallway gets a neighbour when there's room to attach.
        private const int TargetHallwayConnections = 4;
        private static void TopUpHallwayConnections(InteriorLayout layout, BuildingDefinition def,
            Random rng, Dictionary<RoomDefinition, int> quotas)
        {
            var hallways = new List<PlacedRoom>();
            foreach (var r in layout.Rooms)
                if (IsHallway(r.Definition)) hallways.Add(r);

            foreach (var hallway in hallways)
            {
                if (HorizontalConnectionCount(hallway) >= TargetHallwayConnections) continue;

                var pending = new List<SocketDirection>();
                foreach (var defSocket in hallway.Definition.Sockets)
                {
                    if (defSocket.IsVertical()) continue;
                    var worldSocket = SocketDirectionExtensions.Rotate(defSocket, hallway.Rotation);
                    if (!hallway.ConnectedSockets.Contains(worldSocket))
                        pending.Add(worldSocket);
                }
                Shuffle(pending, rng);

                foreach (var worldSocket in pending)
                {
                    if (HorizontalConnectionCount(hallway) >= TargetHallwayConnections) break;
                    var open = new OpenSocket(hallway, worldSocket);
                    TryPlaceNeighbor(layout, def, open, hallway.GridPosition.y, rng,
                        preferSpecial: false, quotas);
                }
            }
        }
        // Bedroom/Bathroom predicates intentionally include their Master- counterparts —
        // adjacency rules like "bathrooms can only touch bedrooms or hallways" historically
        // treated MasterBedroom as a Bedroom (the string Contains check matched both).
        // Walks every empty cell inside the layout's bbox on each floor and places a
        // 1x1 filler hallway at any cell that touches ≥2 already-placed rooms. Each
        // placement also registers connections to every adjacent room so the new tile
        // becomes part of the door graph. Skips when no fillerRoom is configured.
        private static void FillEmptyCellsWithHallway(InteriorLayout layout, BuildingDefinition def)
        {
            var fillerDef = def.FillerRoom;
            if (fillerDef == null) return;
            if (fillerDef.GridSize.x != 1 || fillerDef.GridSize.y != 1) return;

            // Collect candidate empty cells per floor inside the rect (or full grid bbox
            // if no rect constraint). Snapshot first so we don't iterate while mutating.
            int floors = layout.FloorCount;
            int xMin, xMax, zMin, zMax;
            if (layout.RestrictToRectangle && layout.RectMinXZ.HasValue && layout.RectMaxXZ.HasValue)
            {
                xMin = layout.RectMinXZ.Value.x; zMin = layout.RectMinXZ.Value.y;
                xMax = layout.RectMaxXZ.Value.x; zMax = layout.RectMaxXZ.Value.y;
            }
            else
            {
                xMin = int.MaxValue; xMax = int.MinValue; zMin = int.MaxValue; zMax = int.MinValue;
                foreach (var kv in layout.Grid)
                {
                    var c = kv.Key;
                    if (c.x < xMin) xMin = c.x; if (c.x > xMax) xMax = c.x;
                    if (c.z < zMin) zMin = c.z; if (c.z > zMax) zMax = c.z;
                }
                if (xMin > xMax) return;
            }

            for (int y = 0; y < floors; y++)
            for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
            {
                var cell = new Vector3Int(x, y, z);
                if (layout.IsCellOccupied(cell)) continue;
                int neighbours = 0;
                if (layout.Grid.ContainsKey(new Vector3Int(x + 1, y, z))) neighbours++;
                if (layout.Grid.ContainsKey(new Vector3Int(x - 1, y, z))) neighbours++;
                if (layout.Grid.ContainsKey(new Vector3Int(x, y, z + 1))) neighbours++;
                if (layout.Grid.ContainsKey(new Vector3Int(x, y, z - 1))) neighbours++;
                if (neighbours < 2) continue;

                var placed = TryPlaceAt(layout, fillerDef, cell);
                if (placed == null) continue;

                // Register connections to every adjacent room with a matching socket.
                TryRegisterFillerConnection(layout, placed, SocketDirection.North, new Vector3Int(x, y, z + 1));
                TryRegisterFillerConnection(layout, placed, SocketDirection.South, new Vector3Int(x, y, z - 1));
                TryRegisterFillerConnection(layout, placed, SocketDirection.East,  new Vector3Int(x + 1, y, z));
                TryRegisterFillerConnection(layout, placed, SocketDirection.West,  new Vector3Int(x - 1, y, z));
            }
        }

        private static void TryRegisterFillerConnection(InteriorLayout layout, PlacedRoom filler,
            SocketDirection world, Vector3Int neighborCell)
        {
            if (!filler.Definition.HasSocket(world)) return;
            if (filler.ConnectedSockets.Contains(world)) return;
            if (!layout.Grid.TryGetValue(neighborCell, out var neighbor)) return;
            var nbrSocket = world.Opposite();
            // Neighbor needs the matching socket too. Get its def-socket via inverse rotation.
            var nbrDefSocket = nbrSocket.IsVertical()
                ? nbrSocket
                : SocketDirectionExtensions.Rotate(nbrSocket, -neighbor.Rotation);
            if (!neighbor.Definition.HasSocket(nbrDefSocket)) return;
            if (neighbor.ConnectedSockets.Contains(nbrSocket)) return;
            // Don't double-register if neighbor already has a connection on this side
            // pointing somewhere else (shouldn't happen since ConnectedSockets is unique).
            RegisterConnection(layout, filler, world, neighbor, nbrSocket);
        }

        private static bool IsBedroom(RoomDefinition def) =>
            def != null && (def.Kind == RoomKind.Bedroom || def.Kind == RoomKind.MasterBedroom);
        private static bool IsBathroom(RoomDefinition def) =>
            def != null && (def.Kind == RoomKind.Bathroom || def.Kind == RoomKind.MasterBathroom);
        private static bool IsLivingRoom(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.LivingRoom;
        private static bool IsMasterBedroom(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.MasterBedroom;
        private static bool IsMasterBathroom(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.MasterBathroom;
        private static bool IsKitchen(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.Kitchen;
        private static bool IsPantry(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.Pantry;
        private static bool IsMudRoom(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.MudRoom;
        private static bool IsWalkinCloset(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.WalkinCloset;
        private static bool IsEntry(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.Entry;
        private static bool IsGarage(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.Garage;
        private static bool IsSunRoom(RoomDefinition def) =>
            def != null && def.Kind == RoomKind.SunRoom;

        private static int CountWhere(InteriorLayout layout, System.Func<RoomDefinition, bool> pred)
        {
            int n = 0;
            foreach (var r in layout.Rooms)
                if (pred(r.Definition)) n++;
            return n;
        }

        // Shared placement attempt: stays on the current floor, places the room and
        // registers the socket connection. Tries each rotation (shuffled per call) and
        // keeps the first one that fits.
        private static PlacedRoom TryPlaceCandidate(InteriorLayout layout, OpenSocket open,
            SocketDirection needed, RoomDefinition candidate, Random rng, bool enforceHallwaySides)
        {
            foreach (var rot in ShuffledRotations(rng))
            {
                var defSocket = SocketDirectionExtensions.Rotate(needed, -rot);
                if (!candidate.HasSocket(defSocket)) continue;

                // DiningRoom convention: its def-South wall is the "kitchen-facing" wall
                // (the prefab's dining-table anchor is baked there). Reject any rotation
                // where def-South wouldn't end up facing the parent.
                if (candidate.Kind == RoomKind.DiningRoom && defSocket != SocketDirection.South)
                    continue;

                // Candidate-side hallway long-side rule — only valid when we know the
                // candidate's rotation (which determines what's "long" vs "short").
                if (enforceHallwaySides && IsHallway(candidate))
                {
                    var candidateRotatedSize = RotatedSize(candidate.GridSize, rot);
                    if (!IsHallwayShortEnd(candidateRotatedSize, needed)
                        && !IsPrivateRoom(open.Room.Definition))
                        continue;
                }

                var pos = NeighborOrigin(open.Room, open.Socket, candidate, rot);
                if (pos.y != open.Room.GridPosition.y) continue;
                var room = TryPlaceAt(layout, candidate, pos, rot);
                if (room == null) continue;
                // DiningRoom may only spawn if its kitchen-facing side is a LONG side
                // and every cell on that side is inside the Kitchen — i.e. the dining
                // room's full long wall is shared with the Kitchen, not a corner clip.
                if (candidate.Kind == RoomKind.DiningRoom
                    && !DiningSatisfiesKitchenLongSideRule(layout, room, needed, open.Room))
                {
                    UndoPlacement(layout, room);
                    continue;
                }
                RegisterConnection(layout, open.Room, open.Socket, room, needed);
                return room;
            }
            return null;
        }

        // Yields the four rotations in a random order so layouts vary across seeds.
        private static int[] ShuffledRotations(Random rng)
        {
            var arr = new[] { 0, 1, 2, 3 };
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        // Lists required rooms (with positive remaining count) that COULD provide the
        // requested world-socket under some rotation AND are allowed on this floor.
        // Repeated by remaining count so heavier-quota rooms are picked proportionally.
        private static List<RoomDefinition> BuildRequiredCandidates(
            Dictionary<RoomDefinition, int> quotas, SocketDirection needed,
            int floor, int floorCount, int entryFloor)
        {
            var list = new List<RoomDefinition>();
            foreach (var kv in quotas)
            {
                if (kv.Value <= 0 || kv.Key == null) continue;
                if (kv.Key.IsVerticalConnector) continue;       // vertical handled separately
                if (!CanRotateToProvide(kv.Key, needed)) continue;
                if (!kv.Key.AllowedOnFloor(floor, floorCount, entryFloor)) continue;
                for (int i = 0; i < kv.Value; i++) list.Add(kv.Key);
            }
            return list;
        }

        // True if any rotation of `def` would expose a socket pointing in `worldNeeded`.
        // For horizontal sockets this is essentially "does the def have any horizontal
        // socket"; rotation lets us point it wherever we want.
        private static bool CanRotateToProvide(RoomDefinition def, SocketDirection worldNeeded)
        {
            if (worldNeeded.IsVertical()) return def.HasSocket(worldNeeded);
            for (int r = 0; r < 4; r++)
                if (def.HasSocket(SocketDirectionExtensions.Rotate(worldNeeded, -r))) return true;
            return false;
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
                // Use the rotation-aware NeighborOrigin so a connector placed off a
                // rotated parent (e.g. Garage r3) lands with door cells aligned. The
                // legacy 3-arg overload assumes door cells at (0,0) for both rooms,
                // which is only true for non-rotated rooms.
                var pos  = NeighborOrigin(open.Room, open.Socket, connDef, nbrRotation: 0);
>>>>>>> origin/interiors-changes
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

<<<<<<< HEAD
        private static PlacedRoom TryPlaceAt(InteriorLayout layout, RoomDefinition def, Vector3Int pos)
        {
            var room = new PlacedRoom(def, pos);
            foreach (var cell in room.OccupiedCells())
                if (layout.IsCellOccupied(cell)) return null;

=======
        private static PlacedRoom TryPlaceAt(InteriorLayout layout, RoomDefinition def, Vector3Int pos, int rotation = 0)
        {
            // Front-facade constraint — no room may extend south of the entry (z < 0).
            // The entry itself is at z=0; everything else must be at z=0 or further north.
            if (layout.RestrictSouthOfEntry && pos.z < 0) return null;

            var room = new PlacedRoom(def, pos, rotation);
            foreach (var cell in room.OccupiedCells())
                if (layout.IsCellOccupied(cell)) return null;

            // Rectangular-footprint constraint — every cell must lie inside the target box.
            if (layout.RestrictToRectangle && layout.RectMinXZ.HasValue && layout.RectMaxXZ.HasValue)
            {
                var rlo = layout.RectMinXZ.Value;
                var rhi = layout.RectMaxXZ.Value;
                foreach (var cell in room.OccupiedCells())
                {
                    if (cell.x < rlo.x || cell.x > rhi.x || cell.z < rlo.y || cell.z > rhi.y)
                        return null;
                }
            }

            // Front-facade rule — only Entry/LivingRoom/Office/PowderRoom may touch z=0
            // on the entry floor. Private rooms (bedrooms, bathrooms) and back-of-house
            // (kitchen, garage, dining, pantry, etc.) get pushed deeper into the layout.
            if (layout.RestrictFrontFacade && pos.y == layout.EntryFloor && !CanTouchFrontFacade(def))
            {
                foreach (var cell in room.OccupiedCells())
                    if (cell.z == 0) return null;
            }

            // Bounding-silhouette constraint — upper-floor rooms must sit inside the
            // entry-floor footprint's NSEW extents (with 1-cell of slack so a bedroom
            // can hang slightly off the front facade). Stops the upper floor from
            // sprawling past the ground-floor silhouette and looking like it's floating.
            if (layout.RestrictUpperFloorOverhang && pos.y > layout.EntryFloor)
            {
                EnsureEntryFloorBounds(layout);
                if (layout.EntryFloorMin.HasValue && layout.EntryFloorMax.HasValue)
                {
                    const int slack = 1;
                    var lo = layout.EntryFloorMin.Value;
                    var hi = layout.EntryFloorMax.Value;
                    foreach (var cell in room.OccupiedCells())
                    {
                        if (cell.x < lo.x - slack || cell.x > hi.x + slack ||
                            cell.z < lo.y - slack || cell.z > hi.y + slack)
                            return null;
                    }
                }
            }

>>>>>>> origin/interiors-changes
            foreach (var cell in room.OccupiedCells())
                layout.Grid[cell] = room;
            layout.Rooms.Add(room);
            return room;
        }

<<<<<<< HEAD
        private static void RegisterConnection(
            InteriorLayout layout,
            PlacedRoom a, SocketDirection sa,
            PlacedRoom b, SocketDirection sb)
        {
            a.ConnectedSockets.Add(sa);
            b.ConnectedSockets.Add(sb);
            layout.Connections.Add(new InteriorLayout.Connection(a, sa, b, sb));
=======
        // Reverses TryPlaceAt for a placement we don't want to keep (e.g. a DiningRoom
        // that turned out not to share its long side with a Kitchen). Removes both the
        // grid cells and the room itself; doesn't touch ConnectedSockets because we
        // call this BEFORE RegisterConnection runs.
        private static void UndoPlacement(InteriorLayout layout, PlacedRoom room)
        {
            foreach (var cell in room.OccupiedCells())
                layout.Grid.Remove(cell);
            layout.Rooms.Remove(room);
        }

        // True iff `dining` was just placed against `parent` such that every cell along
        // the dining room's `needed`-facing wall (= the wall facing the parent) is a
        // long-side cell AND is adjacent to a `parent` cell. Enforces the "dining room
        // long side fully against kitchen" rule.
        private static bool DiningSatisfiesKitchenLongSideRule(InteriorLayout layout,
            PlacedRoom dining, SocketDirection needed, PlacedRoom parent)
        {
            if (parent.Definition.Kind != RoomKind.Kitchen) return false;
            var rs = dining.RotatedGridSize;
            bool ns = (needed == SocketDirection.North || needed == SocketDirection.South);
            int wallLen  = ns ? rs.x : rs.y;
            int otherLen = ns ? rs.y : rs.x;
            if (wallLen <= otherLen) return false;  // not the long side
            int dx = 0, dz = 0;
            switch (needed)
            {
                case SocketDirection.North: dz =  1; break;
                case SocketDirection.South: dz = -1; break;
                case SocketDirection.East:  dx =  1; break;
                case SocketDirection.West:  dx = -1; break;
                default: return false;
            }
            foreach (var cell in dining.OccupiedCells())
            {
                bool onSide = needed switch
                {
                    SocketDirection.North => cell.z == dining.GridPosition.z + rs.y - 1,
                    SocketDirection.South => cell.z == dining.GridPosition.z,
                    SocketDirection.East  => cell.x == dining.GridPosition.x + rs.x - 1,
                    SocketDirection.West  => cell.x == dining.GridPosition.x,
                    _ => false,
                };
                if (!onSide) continue;
                var beyond = new Vector3Int(cell.x + dx, cell.y, cell.z + dz);
                if (!layout.Grid.TryGetValue(beyond, out var occupant) || occupant != parent)
                    return false;
            }
            return true;
        }

        // Walks the layout's grid once and caches the NSEW extents of the entry-floor
        // cells, so the upper-floor bbox check doesn't re-scan on every placement.
        // Safe to call repeatedly — re-scans only when the cache is empty.
        private static void EnsureEntryFloorBounds(InteriorLayout layout)
        {
            if (layout.EntryFloorMin.HasValue && layout.EntryFloorMax.HasValue) return;
            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;
            bool any = false;
            foreach (var kv in layout.Grid)
            {
                var c = kv.Key;
                if (c.y != layout.EntryFloor) continue;
                any = true;
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.z > maxZ) maxZ = c.z;
            }
            if (!any) return;
            layout.EntryFloorMin = new Vector2Int(minX, minZ);
            layout.EntryFloorMax = new Vector2Int(maxX, maxZ);
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
>>>>>>> origin/interiors-changes
        }

        // ── Position math ──────────────────────────────────────────────────────

        // Computes the grid origin of a neighbor room placed via the given socket.
        // For South/West the formula depends on the neighbor's size.
        public static Vector3Int NeighborOrigin(PlacedRoom from, SocketDirection socket, Vector2Int nbrSize)
        {
<<<<<<< HEAD
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
=======
            // Compatibility shim — preserves the pre-rotation behaviour for legacy
            // callers and tests. Assumes both rooms are non-rotated and the doors sit
            // at the (0,0) door-cell offset that the original math implicitly assumed.
            return NeighborOriginCore(from.GridPosition, from.Rotation,
                from.Definition.GridSize, socket,
                nbrSize, nbrRotation: 0,
                fromDoorOffset: Vector2Int.zero, nbrDoorOffset: Vector2Int.zero);
        }

        // Compute where a neighbor room's grid pivot should land when attaching via the
        // given world-socket of the parent. Accounts for both rooms' rotation: the door
        // cell on each side is offset based on the def's SW-most-cell convention rotated
        // into world coordinates, then the neighbor is positioned so the two door cells
        // align across the shared wall.
        public static Vector3Int NeighborOrigin(PlacedRoom from, SocketDirection worldSocket,
            RoomDefinition nbrDef, int nbrRotation)
        {
            var fromDoor = WorldDoorCellOffset(from.Definition, from.Rotation, worldSocket);
            var nbrDoor  = WorldDoorCellOffset(nbrDef, nbrRotation, worldSocket.Opposite());
            return NeighborOriginCore(from.GridPosition, from.Rotation,
                from.Definition.GridSize, worldSocket,
                nbrDef.GridSize, nbrRotation, fromDoor, nbrDoor);
        }

        private static Vector3Int NeighborOriginCore(Vector3Int p, int fromRotation,
            Vector2Int fromDefSize, SocketDirection worldSocket,
            Vector2Int nbrDefSize, int nbrRotation,
            Vector2Int fromDoorOffset, Vector2Int nbrDoorOffset)
        {
            var fromSize = RotatedSize(fromDefSize, fromRotation);
            var nbrSize  = RotatedSize(nbrDefSize, nbrRotation);
            var fromDoor = fromDoorOffset;
            var nbrDoor  = nbrDoorOffset;

            return worldSocket switch
            {
                SocketDirection.North => new Vector3Int(p.x + fromDoor.x - nbrDoor.x, p.y, p.z + fromSize.y),
                SocketDirection.South => new Vector3Int(p.x + fromDoor.x - nbrDoor.x, p.y, p.z - nbrSize.y),
                SocketDirection.East  => new Vector3Int(p.x + fromSize.x,             p.y, p.z + fromDoor.y - nbrDoor.y),
                SocketDirection.West  => new Vector3Int(p.x - nbrSize.x,              p.y, p.z + fromDoor.y - nbrDoor.y),
                SocketDirection.Up    => new Vector3Int(p.x, p.y + 1, p.z),
                SocketDirection.Down  => new Vector3Int(p.x, p.y - 1, p.z),
                _                     => throw new ArgumentOutOfRangeException(nameof(worldSocket))
            };
        }

        // The grid footprint of a room after applying `rotation` (0..3 quarter-turns).
        public static Vector2Int RotatedSize(Vector2Int defSize, int rotation) =>
            (rotation & 1) == 0
                ? defSize
                : new Vector2Int(defSize.y, defSize.x);

        // The def's door is always placed at the SW-most cell of each def-wall. This
        // returns the WORLD cell (relative to the rotated room's pivot) that hosts the
        // door for the given world socket. Used by NeighborOrigin to align doors and by
        // DoorTransform to render the door at the right world position.
        public static Vector2Int WorldDoorCellOffset(RoomDefinition def, int rotation, SocketDirection worldSocket)
        {
            if (worldSocket.IsVertical()) return Vector2Int.zero;
            var s = def.GridSize;
            var defSocket = SocketDirectionExtensions.Rotate(worldSocket, -rotation);
            Vector2Int defCell = defSocket switch
            {
                SocketDirection.North => new Vector2Int(0, s.y - 1),
                SocketDirection.South => new Vector2Int(0, 0),
                SocketDirection.East  => new Vector2Int(s.x - 1, 0),
                SocketDirection.West  => new Vector2Int(0, 0),
                _ => Vector2Int.zero,
            };
            return RotateCell(defCell, rotation, s);
        }

        // Rotate a def-local cell coordinate into the rotated room's local cell frame.
        // The rotated room always occupies the +X +Z quadrant relative to its pivot.
        public static Vector2Int RotateCell(Vector2Int defCell, int rotation, Vector2Int defSize)
        {
            int x = defCell.x, z = defCell.y, sx = defSize.x, sy = defSize.y;
            int r = ((rotation % 4) + 4) % 4;
            return r switch
            {
                0 => new Vector2Int(x,            z),
                1 => new Vector2Int(z,            sx - 1 - x),
                2 => new Vector2Int(sx - 1 - x,   sy - 1 - z),
                3 => new Vector2Int(sy - 1 - z,   x),
                _ => new Vector2Int(x, z),
>>>>>>> origin/interiors-changes
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
<<<<<<< HEAD
        private const float DoorWidth = 2f;
=======
        private const float DoorWidth = 1.7f;
>>>>>>> origin/interiors-changes

        public static (Vector3 position, Quaternion rotation) DoorTransform(
            InteriorLayout.Connection conn, Vector3 buildingOrigin, BuildingDefinition def)
        {
            var a = conn.RoomA;
            var cell = def.GridCellMeters;
            var floorH = def.FloorHeightMeters;
            var origin = buildingOrigin;
            var p = a.GridPosition;
<<<<<<< HEAD
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
=======
            var rs = a.RotatedGridSize;
            float floorY = origin.y + p.y * floorH;

            // The door cell along the wall is determined by the def's SW-most-cell
            // convention rotated into world coords (so a rotated room's door lands in
            // the right column/row of its world-facing wall).
            var doorCell = WorldDoorCellOffset(a.Definition, a.Rotation, conn.SocketA);

            Vector3 openingCentre;
            Quaternion rot;
            switch (conn.SocketA)
            {
                case SocketDirection.North:
                    openingCentre = new Vector3(origin.x + (p.x + doorCell.x + 0.5f) * cell, floorY, origin.z + (p.z + rs.y) * cell);
                    rot = Quaternion.Euler(0, 0, 0);
                    break;
                case SocketDirection.South:
                    openingCentre = new Vector3(origin.x + (p.x + doorCell.x + 0.5f) * cell, floorY, origin.z + p.z * cell);
                    rot = Quaternion.Euler(0, 180, 0);
                    break;
                case SocketDirection.East:
                    openingCentre = new Vector3(origin.x + (p.x + rs.x) * cell, floorY, origin.z + (p.z + doorCell.y + 0.5f) * cell);
                    rot = Quaternion.Euler(0, 90, 0);
                    break;
                case SocketDirection.West:
                    openingCentre = new Vector3(origin.x + p.x * cell, floorY, origin.z + (p.z + doorCell.y + 0.5f) * cell);
>>>>>>> origin/interiors-changes
                    rot = Quaternion.Euler(0, 270, 0);
                    break;
                default: // Up/Down — centre of vertical connector room
                    return (
<<<<<<< HEAD
                        new Vector3(origin.x + (p.x + s.x * 0.5f) * cell, floorY + floorH * 0.5f, origin.z + (p.z + s.y * 0.5f) * cell),
=======
                        new Vector3(origin.x + (p.x + rs.x * 0.5f) * cell, floorY + floorH * 0.5f, origin.z + (p.z + rs.y * 0.5f) * cell),
>>>>>>> origin/interiors-changes
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
<<<<<<< HEAD
            foreach (var s in room.Definition.Sockets)
                if (!room.ConnectedSockets.Contains(s))
                    frontier.Add(new OpenSocket(room, s));
=======
            // The frontier carries WORLD sockets (so neighbour-placement math doesn't have
            // to keep converting). For non-vertical sockets, rotate the def-socket into the
            // room's world orientation before comparing against ConnectedSockets (which
            // also stores world sockets).
            //
            // Connection cap: rooms like a bathroom have maxHorizontalConnections=1 so they
            // only ever get one door. The placement connection counts; once it's hit, we
            // skip adding the room's remaining sockets to the frontier so nothing else can
            // attach to it.
            int cap = room.Definition.MaxHorizontalConnections;
            if (cap >= 0 && HorizontalConnectionCount(room) >= cap) return;

            foreach (var defSocket in room.Definition.Sockets)
            {
                var worldSocket = defSocket.IsVertical()
                    ? defSocket
                    : SocketDirectionExtensions.Rotate(defSocket, room.Rotation);
                if (!room.ConnectedSockets.Contains(worldSocket))
                    frontier.Add(new OpenSocket(room, worldSocket));
            }
        }

        // Count non-vertical entries in a room's ConnectedSockets — i.e. how many doors
        // already exist into this room.
        private static int HorizontalConnectionCount(PlacedRoom room)
        {
            int n = 0;
            foreach (var s in room.ConnectedSockets)
                if (!s.IsVertical()) n++;
            return n;
>>>>>>> origin/interiors-changes
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
<<<<<<< HEAD
            SocketDirection needed, bool vertical, bool preferSpecial)
=======
            SocketDirection needed, bool vertical, bool preferSpecial,
            int floor = -1, int floorCount = -1, int entryFloor = -1)
>>>>>>> origin/interiors-changes
        {
            var list = new List<RoomDefinition>();
            foreach (var rd in pool)
            {
                if (rd.IsVerticalConnector != vertical) continue;
<<<<<<< HEAD
                if (!rd.HasSocket(needed)) continue;
                if (preferSpecial && rd.Category != RoomCategory.Special) continue;
=======
                // Vertical connectors aren't rotated, so they need the literal socket. All
                // others might rotate to provide it.
                if (vertical ? !rd.HasSocket(needed) : !CanRotateToProvide(rd, needed)) continue;
                if (preferSpecial && rd.Category != RoomCategory.Special) continue;
                // Floor-restricted rooms are filtered out when caller knows the floor.
                if (floor >= 0 && !rd.AllowedOnFloor(floor, floorCount, entryFloor)) continue;
>>>>>>> origin/interiors-changes
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
