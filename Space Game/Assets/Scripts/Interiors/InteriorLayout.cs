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
        // When true, room placement rejects any grid position with z < 0 â€” the entry
        // sits at z=0 and the southern facade of the building is the entry's south face.
        public bool RestrictSouthOfEntry { get; set; }
        // When true, every cell of a room placed above the entry floor must sit inside
        // the bounding rectangle of the entry-floor footprint (NSEW extents), with one
        // cell of slack on each side. Keeps the upper floor inside the house silhouette
        // without forcing strict cell-over-cell support â€” bedrooms can shift around.
        public bool RestrictUpperFloorOverhang { get; set; }
        // When true, every cell of every room must fall inside the axis-aligned target
        // rectangle (RectMinXZ..RectMaxXZ inclusive). Produces house-style rectangular
        // footprints with no L-shapes. Min/Max are computed once in TryGenerate based on
        // the building's MaxRooms count.
        public bool RestrictToRectangle { get; set; }
        public Vector2Int? RectMinXZ { get; set; }
        public Vector2Int? RectMaxXZ { get; set; }
        // When true, only Entry / LivingRoom / Office / PowderRoom may have cells at z=0
        // on the entry floor. Forces private rooms and the kitchen / garage back from
        // the street so the southern facade reads as the front of the house.
        public bool RestrictFrontFacade { get; set; }
        // Cached entry-floor bounding box (min/max grid coords on x/z), populated lazily
        // the first time an upper-floor placement is attempted. Entry-floor cells are
        // added before upper-floor placement begins, so the box is stable once computed.
        public Vector2Int? EntryFloorMin { get; set; }
        public Vector2Int? EntryFloorMax { get; set; }
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
            // True when this connection should render as an open archway â€” no door, and
            // wall panels on both sides of the shared boundary are removed. Used for the
            // residential entry â†’ living-room transition so it feels like one space.
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
