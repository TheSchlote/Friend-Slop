namespace FriendSlop.Interiors
{
    // Where in a room a furniture anchor sits. Used by the spawner to filter
    // FurnitureDefinitions whose own `placement` doesn't match.
    public enum AnchorPlacement
    {
        Wall,         // Against an interior wall; furniture's back faces the wall.
        Corner,       // In a corner; for tall/narrow pieces (lamps, plants, shelves).
        Center,       // Free-floating in the room centre (tables, rugs).
        Tabletop,     // Sits on top of another piece (vase on a table, microwave on counter).
        AroundTable,  // Floor-level slot around a table — dining chairs facing the table.
    }
}
