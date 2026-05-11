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
        public int EntryFloor { get; set; }
        // The room that holds the building's exterior door, and which of its sockets the
        // exit uses. Null when no exterior door was reserved (legacy/test buildings).
        public PlacedRoom ExitRoom         { get; set; }
        public SocketDirection? ExitSocket { get; set; }

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
            // True when this connection should render as an open archway — no door, and
            // wall panels on both sides of the shared boundary are removed. Used for the
            // residential entry → living-room transition so it feels like one space.
            public readonly bool IsOpenPassage;

            public Connection(PlacedRoom a, SocketDirection sa, PlacedRoom b, SocketDirection sb,
                bool isOpenPassage = false)
            {
                RoomA = a; SocketA = sa; RoomB = b; SocketB = sb;
                IsOpenPassage = isOpenPassage;
            }
        }
    }
}
