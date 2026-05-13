using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Represents a single room placed at a grid position during generation.
    // GridPosition.x = column, GridPosition.y = floor, GridPosition.z = row.
    // Rotation is 0..3 quarter-turns clockwise (looking down the +Y axis).
    // The room occupies RotatedGridSize.x * RotatedGridSize.y cells starting from GridPosition.
    public sealed class PlacedRoom
    {
        public RoomDefinition Definition     { get; }
        public Vector3Int GridPosition       { get; }
        public int Rotation                  { get; }
        public HashSet<SocketDirection> ConnectedSockets { get; } = new();

        public PlacedRoom(RoomDefinition definition, Vector3Int gridPosition, int rotation = 0)
        {
            Definition   = definition;
            GridPosition = gridPosition;
            Rotation     = ((rotation % 4) + 4) % 4;
        }

        // Footprint after rotation. Quarter turns swap width and depth.
        public Vector2Int RotatedGridSize =>
            (Rotation & 1) == 0
                ? Definition.GridSize
                : new Vector2Int(Definition.GridSize.y, Definition.GridSize.x);

        public IEnumerable<Vector3Int> OccupiedCells()
        {
            var s = RotatedGridSize;
            for (int x = 0; x < s.x; x++)
                for (int z = 0; z < s.y; z++)
                    yield return GridPosition + new Vector3Int(x, 0, z);
        }

        // True if the room has the given socket in WORLD coords (i.e. after rotation).
        // Vertical sockets aren't rotated.
        public bool HasWorldSocket(SocketDirection world)
        {
            if (world.IsVertical()) return Definition.HasSocket(world);
            return Definition.HasSocket(SocketDirectionExtensions.Rotate(world, -Rotation));
        }
    }
}
