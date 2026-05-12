using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    public enum FloorRestriction
    {
        Any,
        TopFloorOnly,       // e.g. Office ServerRoom — only on the highest floor
        BottomFloorOnly,    // e.g. Residential Basement contents
        EntryFloorOnly,     // ground/entry floor only, useful for Lobby-adjacent rooms
    }

    // Per-kind furniture constraint for a RoomDefinition. Min forces a minimum number of
    // pieces of `Kind` to spawn (Bedroom: 1 bed). Max caps the count (Bathroom: max 1 toilet).
    // Max == 0 means no upper bound.
    [System.Serializable]
    public struct FurnitureRule
    {
        [SerializeField] private string kind;
        [SerializeField, UnityEngine.Min(0)] private int min;
        [SerializeField, UnityEngine.Min(0)] private int max;

        public string Kind => kind ?? "";
        public int Min => UnityEngine.Mathf.Max(0, min);
        public int Max => UnityEngine.Mathf.Max(0, max);

        public FurnitureRule(string kind, int min, int max)
        {
            this.kind = kind ?? "";
            this.min = UnityEngine.Mathf.Max(0, min);
            this.max = UnityEngine.Mathf.Max(0, max);
        }
    }

    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Room Definition", fileName = "Room")]
    public class RoomDefinition : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector2Int gridSize = Vector2Int.one;
        [SerializeField] private RoomCategory category;
        [SerializeField] private SocketDirection[] sockets = System.Array.Empty<SocketDirection>();
        [SerializeField] private bool isVerticalConnector;
        [SerializeField, Range(1, 100)] private int weight = 10;
        [Tooltip("Maximum number of horizontal connections this room can have. -1 (default) " +
                 "means unlimited — each free wall socket can become a door. Set to 1 for rooms " +
                 "like a bathroom or powder room where only one door makes sense.")]
        [SerializeField] private int maxHorizontalConnections = -1;
        [Tooltip("If true, this room can be picked as the building's entry even when its " +
                 "category isn't Entry (e.g. a LivingRoom that doubles as a foyer).")]
        [SerializeField] private bool isEntryCandidate;
        [Tooltip("Restricts which floor this room can spawn on. Useful for ServerRooms " +
                 "(top floor), Lobby annexes (entry floor), etc.")]
        [SerializeField] private FloorRestriction floorRestriction = FloorRestriction.Any;

        [Header("Furniture")]
        [Tooltip("Furniture is eligible to spawn here if any of its tags overlap with " +
                 "this list. Every room should include FurnitureTags.Shared so the universal " +
                 "(lamps, plants, etc.) pool is available.")]
        [SerializeField] private string[] furnitureTags = System.Array.Empty<string>();
        [Tooltip("Random number of furniture pieces spawned in this room (clamped by available anchors).")]
        [SerializeField] private Vector2Int furnitureCountRange = new Vector2Int(2, 4);
        [Tooltip("Per-kind constraints. A rule with min>0 forces that many pieces of the kind " +
                 "to spawn (Bedroom: bed×1). A rule with max>0 caps spawns (Bedroom: bed max=1).")]
        [SerializeField] private FurnitureRule[] furnitureRules = System.Array.Empty<FurnitureRule>();

        public GameObject Prefab         => prefab;
        public Vector2Int GridSize       => gridSize;
        public RoomCategory Category     => category;
        public IReadOnlyList<SocketDirection> Sockets => sockets;
        public bool IsVerticalConnector  => isVerticalConnector;
        public int Weight                => weight;
        public int MaxHorizontalConnections => maxHorizontalConnections;
        public bool IsEntryCandidate     => isEntryCandidate || category == RoomCategory.Entry;
        public FloorRestriction FloorRestriction => floorRestriction;
        public IReadOnlyList<string> FurnitureTags => furnitureTags ?? System.Array.Empty<string>();
        public Vector2Int FurnitureCountRange     => furnitureCountRange;
        public IReadOnlyList<FurnitureRule> FurnitureRules =>
            furnitureRules ?? System.Array.Empty<FurnitureRule>();

        public bool HasSocket(SocketDirection dir)
        {
            foreach (var s in sockets)
                if (s == dir) return true;
            return false;
        }

        // True if this room can spawn on the given floor (inclusive), given the layout's
        // floor count and entry floor.
        public bool AllowedOnFloor(int floor, int floorCount, int entryFloor)
        {
            switch (floorRestriction)
            {
                case FloorRestriction.TopFloorOnly:    return floor == floorCount - 1;
                case FloorRestriction.BottomFloorOnly: return floor == 0;
                case FloorRestriction.EntryFloorOnly:  return floor == entryFloor;
                default:                                return true;
            }
        }
    }
}
