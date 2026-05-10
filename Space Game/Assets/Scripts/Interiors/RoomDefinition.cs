using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Room Definition", fileName = "Room")]
    public class RoomDefinition : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector2Int gridSize = Vector2Int.one;
        [SerializeField] private RoomCategory category;
        [SerializeField] private SocketDirection[] sockets = System.Array.Empty<SocketDirection>();
        [SerializeField] private bool isVerticalConnector;
        [SerializeField, Range(1, 100)] private int weight = 10;

        public GameObject Prefab         => prefab;
        public Vector2Int GridSize       => gridSize;
        public RoomCategory Category     => category;
        public IReadOnlyList<SocketDirection> Sockets => sockets;
        public bool IsVerticalConnector  => isVerticalConnector;
        public int Weight                => weight;

        public bool HasSocket(SocketDirection dir)
        {
            foreach (var s in sockets)
                if (s == dir) return true;
            return false;
        }
    }
}
