namespace FriendSlop.Interiors
{
    public enum RoomCategory { Entry, Generic, Special, Utility }
<<<<<<< HEAD
=======

    // Functional identity of a room, used by adjacency / restriction logic in the
    // layout generator. Replaces brittle name-substring checks like
    // `def.name.Contains("Bedroom")` which silently break on rename and substring
    // collisions (MasterBedroom contains "Bedroom"). Default value is Unspecified
    // for rooms (legacy / test fixtures) that don't opt in.
    public enum RoomKind
    {
        Unspecified,
        Entry,
        Hallway,
        LivingRoom,
        Kitchen,
        DiningRoom,
        Bedroom,
        MasterBedroom,
        Bathroom,
        MasterBathroom,
        PowderRoom,
        Pantry,
        WalkinCloset,
        LinenCloset,
        Laundry,
        Office,
        Library,
        Den,
        SunRoom,
        GameRoom,
        Garage,
        Basement,
        Workshop,
        WineCellar,
        MechanicalRoom,
        MudRoom,
        Stair,
    }
>>>>>>> origin/interiors-changes
}
