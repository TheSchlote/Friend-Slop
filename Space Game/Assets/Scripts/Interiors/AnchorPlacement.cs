namespace FriendSlop.Interiors
{
    // Where in a room a furniture anchor sits. Used by the spawner to filter
    // FurnitureDefinitions whose own `placement` doesn't match.
    public enum AnchorPlacement
    {
        Wall,         // Against an interior wall; furniture's back faces the wall.
        Corner,       // In a corner; for tall/narrow pieces (lamps, plants, shelves).
        Center,       // Free-floating in the room centre (tables, rugs).
    }
}
