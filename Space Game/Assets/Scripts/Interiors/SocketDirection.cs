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

        // Rotates a socket by `quarterTurns` 90Â° clockwise turns (looking down the +Y axis).
        // North -> East -> South -> West -> North under repeated CW rotation. Vertical
        // sockets pass through unchanged. Negative quarter-turns rotate counter-clockwise.
        public static SocketDirection Rotate(SocketDirection d, int quarterTurns)
        {
            if (d.IsVertical()) return d;
            int cwOrder = d switch
            {
                SocketDirection.North => 0,
                SocketDirection.East  => 1,
                SocketDirection.South => 2,
                SocketDirection.West  => 3,
                _ => 0,
            };
            int rotated = (((cwOrder + quarterTurns) % 4) + 4) % 4;
            return rotated switch
            {
                0 => SocketDirection.North,
                1 => SocketDirection.East,
                2 => SocketDirection.South,
                3 => SocketDirection.West,
                _ => SocketDirection.North,
            };
        }
    }
}
