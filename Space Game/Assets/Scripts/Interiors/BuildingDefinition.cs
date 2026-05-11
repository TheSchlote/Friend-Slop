using System.Collections.Generic;
using UnityEngine;

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
        [SerializeField] private float gridCellMeters = 8f;
        [SerializeField] private RoomDefinition[] roomPool = System.Array.Empty<RoomDefinition>();
        [SerializeField, Range(0, 5)] private int minSpecialRooms;
        [SerializeField, Range(0, 5)] private int maxSpecialRooms = 1;

        public string DisplayName        => displayName;
        public int MinRooms              => minRooms;
        public int MaxRooms              => maxRooms;
        public int MinFloors             => minFloors;
        public int MaxFloors             => maxFloors;
        public float FloorHeightMeters   => floorHeightMeters;
        public float GridCellMeters      => gridCellMeters;
        public IReadOnlyList<RoomDefinition> RoomPool => roomPool;
        public int MinSpecialRooms       => minSpecialRooms;
        public int MaxSpecialRooms       => maxSpecialRooms;
    }
}
