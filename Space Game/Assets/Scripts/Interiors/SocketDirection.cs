using UnityEngine;

namespace FriendSlop.Interiors
{
    public enum SocketDirection { North, South, East, West, Up, Down }

    public static class SocketDirectionExtensions
    {
        public static SocketDirection Opposite(this SocketDirection d) => d switch
        {
            SocketDirection.North => SocketDirection.South,
            SocketDirection.South => SocketDirection.North,
            SocketDirection.East  => SocketDirection.West,
            SocketDirection.West  => SocketDirection.East,
            SocketDirection.Up    => SocketDirection.Down,
            SocketDirection.Down  => SocketDirection.Up,
            _                    => throw new System.ArgumentOutOfRangeException(nameof(d))
        };

        public static bool IsVertical(this SocketDirection d) =>
            d == SocketDirection.Up || d == SocketDirection.Down;
    }
}
