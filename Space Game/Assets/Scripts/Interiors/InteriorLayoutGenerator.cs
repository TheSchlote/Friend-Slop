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

            var roomsPerFloor = DistributeRooms(totalRooms, floors, rng);
            var frontier      = new List<OpenSocket>();

            var entryDef = PickByCategory(def.RoomPool, RoomCategory.Entry, rng);
            if (entryDef == null) return null;

            var entry = TryPlaceAt(layout, entryDef, Vector3Int.zero);
            if (entry == null) return null;

            // Reserve a socket on the entry for the exterior exit door — the generator
            // won't try to connect an interior room there, and the exit door fills the
            // wall opening at runtime.
            if (reservedExitSocket.HasValue && entryDef.HasSocket(reservedExitSocket.Value))
                entry.ConnectedSockets.Add(reservedExitSocket.Value);

            AddSockets(frontier, entry);

            int placedOnFloor = 1;

            for (int floor = 0; floor < floors; floor++)
            {
                int target = roomsPerFloor[floor];
                int iters  = 0;

                while (placedOnFloor < target && iters < MaxExpansionIterations)
                {
                    iters++;
                    var floorFrontier = FloorHorizontalFrontier(frontier, floor);
                    if (floorFrontier.Count == 0) break;

                    var open = floorFrontier[rng.Next(floorFrontier.Count)];
                    frontier.Remove(open);

                    bool wantSpecial = layout.CountByCategory(RoomCategory.Special) < targetSpecial;
                    var placed = TryPlaceNeighbor(layout, def, open, floor, rng, wantSpecial);
                    if (placed != null)
                    {
                        AddSockets(frontier, placed);
                        placedOnFloor++;
                    }
                }

                // Connect to next floor via a vertical connector
                if (floor < floors - 1)
                {
                    var connDef = PickVerticalConnector(def.RoomPool, rng);
                    if (connDef == null) return null;

                    var placed = ForceConnector(layout, def, frontier, connDef, floor, rng);
                    if (placed == null) return null;

                    // Mirror the connector on the floor above to close the loop
                    var upPos  = new Vector3Int(placed.GridPosition.x, floor + 1, placed.GridPosition.z);
                    var upRoom = TryPlaceAt(layout, connDef, upPos);
                    if (upRoom == null) return null;

                    RegisterConnection(layout, placed, SocketDirection.Up, upRoom, SocketDirection.Down);
                    AddSockets(frontier, upRoom);
                    placedOnFloor = 1;
                }
            }

            if (layout.CountByCategory(RoomCategory.Special) < def.MinSpecialRooms) return null;
            return layout.Rooms.Count > 0 ? layout : null;
        }

        // ── Placement helpers ──────────────────────────────────────────────────

        private static PlacedRoom TryPlaceNeighbor(
            InteriorLayout layout, BuildingDefinition def, OpenSocket open,
            int floor, Random rng, bool preferSpecial)
        {
            var needed = open.Socket.Opposite();

            var candidates = BuildCandidateList(def.RoomPool, needed, vertical: false, preferSpecial);
            if (candidates.Count == 0 && preferSpecial)
                candidates = BuildCandidateList(def.RoomPool, needed, vertical: false, preferSpecial: false);
            if (candidates.Count == 0) return null;

            Shuffle(candidates, rng);

            foreach (var candidate in candidates)
            {
                var pos = NeighborOrigin(open.Room, open.Socket, candidate.GridSize);
                if (pos.y != open.Room.GridPosition.y) continue; // stay on current floor
                var room = TryPlaceAt(layout, candidate, pos);
                if (room != null)
                {
                    RegisterConnection(layout, open.Room, open.Socket, room, needed);
                    return room;
                }
            }
            return null;
        }

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
            PlacedRoom b, SocketDirection sb)
        {
            a.ConnectedSockets.Add(sa);
            b.ConnectedSockets.Add(sb);
            layout.Connections.Add(new InteriorLayout.Connection(a, sa, b, sb));
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
            SocketDirection needed, bool vertical, bool preferSpecial)
        {
            var list = new List<RoomDefinition>();
            foreach (var rd in pool)
            {
                if (rd.IsVerticalConnector != vertical) continue;
                if (!rd.HasSocket(needed)) continue;
                if (preferSpecial && rd.Category != RoomCategory.Special) continue;
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
