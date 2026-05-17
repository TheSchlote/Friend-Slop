namespace FriendSlop.Interiors.Blocks
{
    // The seven block types available in the residential block system.
    //   Floor:     1×1 cell tile. Carries spawn tags + a label. Walls are NOT
    //              auto-added around floor tiles — user places them manually.
    //   Wall:      Edge-mounted full-height wall segment. Solid.
    //   Door:      Edge-mounted swing-door (replaces a wall on that edge).
    //   Window:    Edge-mounted decorative window (no collision swap).
    //   Stair:     Cell-occupying staircase. Connects to floor above via the
    //              FloorHole on the next floor up (designer places both).
    //   FloorHole: Marks a cell as "no floor here" so stair stacks and balconies
    //              don't show a tile blocking the staircase from above.
    //   Ceiling:   Optional — defaults to auto-spawned above each floor tile,
    //              but explicit Ceiling blocks let you put a ceiling without
    //              a floor underneath (overhang). v1 ignores this; reserved.
    public enum BlockKind
    {
        Floor     = 0,
        Wall      = 1,
        Door      = 2,
        Window    = 3,
        Stair     = 4,
        FloorHole = 5,
        Ceiling   = 6,
    }

    public static class BlockKindExtensions
    {
        // True if the block sits on a cell edge (Wall/Door/Window), false if it
        // occupies the cell itself (Floor/Stair/FloorHole/Ceiling). The cursor
        // uses this to decide between cell-snap and edge-snap behaviour.
        public static bool IsEdgeMounted(this BlockKind kind) =>
            kind == BlockKind.Wall || kind == BlockKind.Door || kind == BlockKind.Window;
    }
}
