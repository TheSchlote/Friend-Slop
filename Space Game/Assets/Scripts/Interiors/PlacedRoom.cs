using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Represents a single room placed at a grid position during generation.
    // GridPosition.x = column, GridPosition.y = floor, GridPosition.z = row.
    // The room occupies GridSize.x * GridSize.y cells starting from GridPosition.
    public sealed class PlacedRoom
    {
        public RoomDefinition Definition     { get; }
        public Vector3Int GridPosition       { get; }
        public HashSet<SocketDirection> ConnectedSockets { get; } = new();

        public PlacedRoom(RoomDefinition definition, Vector3Int gridPosition)
        {
            Definition   = definition;
            GridPosition = gridPosition;
        }

        public IEnumerable<Vector3Int> OccupiedCells()
        {
            var s = Definition.GridSize;
            for (int x = 0; x < s.x; x++)
                for (int z = 0; z < s.y; z++)
                    yield return GridPosition + new Vector3Int(x, 0, z);
        }
    }
}
