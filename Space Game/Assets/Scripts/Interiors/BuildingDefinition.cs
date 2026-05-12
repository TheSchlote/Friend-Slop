using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace FriendSlop.Interiors
{
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Building Definition", fileName = "Building")]
    public class BuildingDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Building";
        [SerializeField, Range(1, 100)] private int minRooms = 4;
        [SerializeField, Range(1, 100)] private int maxRooms = 12;
        [SerializeField, Range(1, 10)]  private int minFloors = 1;
        [SerializeField, Range(1, 10)]  private int maxFloors = 2;
        [SerializeField] private float floorHeightMeters = 4f;
        [SerializeField] private float gridCellMeters = 3.4f;

        [Header("Recipe")]
        [Tooltip("Rooms that must appear in every generated layout. The entry room (RoomCategory.Entry) is " +
                 "picked from here too.")]
        [SerializeField] private RequiredRoom[] requiredRooms = Array.Empty<RequiredRoom>();
        [Tooltip("Rooms used to fill remaining floor space after required rooms are placed. Weighted picks.")]
        [FormerlySerializedAs("roomPool")]
        [SerializeField] private RoomDefinition[] optionalPool = Array.Empty<RoomDefinition>();

        [Header("Special-room targets (within the recipe)")]
        [SerializeField, Range(0, 10)] private int minSpecialRooms;
        [SerializeField, Range(0, 10)] private int maxSpecialRooms = 1;

        [Header("Vertical-link specialisation (optional)")]
        [Tooltip("When going down a floor, use this prefab as the lower mirror instead of " +
                 "duplicating the connector. Use to make e.g. a single 2x2 basement room " +
                 "below a 1x1 stair on the floor above.")]
        [SerializeField] private RoomDefinition downwardConnectorMirror;
        [Tooltip("When going down, the connector on the upper floor must be placed adjacent " +
                 "to one of these rooms (if any are present in the layout). Falls back to any " +
                 "free socket if none of these are placed yet.")]
        [SerializeField] private RoomDefinition[] downConnectorParents = Array.Empty<RoomDefinition>();
        [Tooltip("If true, the basement floor (lower of the down-link) is left with only the " +
                 "mirror room — no further expansion. Use when the basement is a single room.")]
        [SerializeField] private bool skipBasementExpansion;
        [Tooltip("When true (default), the layout generator prefers placing new rooms adjacent to " +
                 "multiple existing rooms, producing compact rectangular / L-shaped footprints like " +
                 "real houses. Disable for buildings that should sprawl irregularly.")]
        [SerializeField] private bool compactLayout = true;
        [Tooltip("When true, only connections that involve a Bedroom, Bathroom, Stair, or Basement " +
                 "get a door + framed wall. All other connections render as open archways. Use for " +
                 "residential houses where the kitchen/dining/living-room are open-plan.")]
        [SerializeField] private bool doorsOnlyForPrivateRooms;
        [Tooltip("When true, the layout generator forbids any room from spawning south of the " +
                 "entry room (z < entry.z). The entry's south face becomes the building's front " +
                 "facade — what a normal house looks like.")]
        [SerializeField] private bool entryAtSouthernEdge;
        [Tooltip("When true, every cell on an upper floor must sit directly above a cell on the " +
                 "floor immediately below. Prevents upper rooms from cantilevering off the ground " +
                 "floor and looking like they're floating.")]
        [SerializeField] private bool restrictUpperFloorOverhang;

        [Header("Phase 3+ hooks (optional)")]
        [Tooltip("Tint applied to wall/floor materials at runtime so each type reads as visually distinct.")]
        [SerializeField] private Color themeColor = Color.white;
        [Tooltip("Loot table used when populating this building. Wired in a later phase.")]
        [SerializeField] private ScriptableObject lootTable;
        [Tooltip("Monster pool used when populating this building. Wired in a later phase.")]
        [SerializeField] private ScriptableObject monsterPool;
        [Tooltip("Round objective override for missions inside this building. Wired in a later phase.")]
        [SerializeField] private ScriptableObject objective;

        public string DisplayName        => displayName;
        public int MinRooms              => minRooms;
        public int MaxRooms              => maxRooms;
        public int MinFloors             => minFloors;
        public int MaxFloors             => maxFloors;
        public float FloorHeightMeters   => floorHeightMeters;
        public float GridCellMeters      => gridCellMeters;
        public IReadOnlyList<RequiredRoom> RequiredRooms => requiredRooms;
        public IReadOnlyList<RoomDefinition> OptionalPool => optionalPool;
        public int MinSpecialRooms       => minSpecialRooms;
        public int MaxSpecialRooms       => maxSpecialRooms;
        public Color ThemeColor          => themeColor;
        public ScriptableObject LootTable => lootTable;
        public ScriptableObject MonsterPool => monsterPool;
        public ScriptableObject Objective => objective;
        public RoomDefinition DownwardConnectorMirror => downwardConnectorMirror;
        public IReadOnlyList<RoomDefinition> DownConnectorParents => downConnectorParents ?? Array.Empty<RoomDefinition>();
        public bool SkipBasementExpansion => skipBasementExpansion;
        public bool CompactLayout         => compactLayout;
        public bool DoorsOnlyForPrivateRooms => doorsOnlyForPrivateRooms;
        public bool EntryAtSouthernEdge   => entryAtSouthernEdge;
        public bool RestrictUpperFloorOverhang => restrictUpperFloorOverhang;

        // Back-compat shim — older code reads RoomPool as the full pool. Returns required ∪ optional
        // so existing callers (tests, scene builders) keep finding every room the building can produce.
        public IReadOnlyList<RoomDefinition> RoomPool
        {
            get
            {
                var combined = new List<RoomDefinition>(optionalPool);
                foreach (var req in requiredRooms)
                    if (req.Definition != null && !combined.Contains(req.Definition))
                        combined.Add(req.Definition);
                return combined;
            }
        }

        [Serializable]
        public struct RequiredRoom
        {
            [SerializeField] private RoomDefinition definition;
            [SerializeField, Min(1)] private int count;
            [Tooltip("Optional. If non-empty, this room is placed adjacent to one of the listed " +
                     "rooms (whichever is already in the layout). Used for relationships like " +
                     "DiningRoom-adjacent-to-Kitchen.")]
            [SerializeField] private RoomDefinition[] adjacentToAny;

            public RoomDefinition Definition => definition;
            public int Count => Mathf.Max(1, count);
            public IReadOnlyList<RoomDefinition> AdjacentToAny =>
                adjacentToAny ?? Array.Empty<RoomDefinition>();

            public RequiredRoom(RoomDefinition def, int count, RoomDefinition[] adjacentToAny = null)
            {
                this.definition = def;
                this.count = Mathf.Max(1, count);
                this.adjacentToAny = adjacentToAny;
            }
        }
    }
}
