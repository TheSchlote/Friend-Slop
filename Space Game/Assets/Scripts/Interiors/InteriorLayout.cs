using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    public sealed class InteriorLayout
    {
        // Maps every occupied grid cell to the room that owns it.
        public Dictionary<Vector3Int, PlacedRoom> Grid { get; } = new();
        // Unique room instances (a multi-cell room appears once here).
        public List<PlacedRoom> Rooms { get; } = new();
        public List<Connection> Connections { get; } = new();
        public int Seed       { get; set; }
        public int FloorCount { get; set; }

        public bool IsCellOccupied(Vector3Int cell) => Grid.ContainsKey(cell);

        public int CountByCategory(RoomCategory cat)
        {
            int n = 0;
            foreach (var r in Rooms)
                if (r.Definition.Category == cat) n++;
            return n;
        }

        public readonly struct Connection
        {
            public readonly PlacedRoom RoomA;
            public readonly SocketDirection SocketA;
            public readonly PlacedRoom RoomB;
            public readonly SocketDirection SocketB;

            public Connection(PlacedRoom a, SocketDirection sa, PlacedRoom b, SocketDirection sb)
            {
                RoomA = a; SocketA = sa; RoomB = b; SocketB = sb;
            }
        }
    }
}
