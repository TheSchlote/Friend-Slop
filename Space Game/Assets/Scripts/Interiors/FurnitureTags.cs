namespace FriendSlop.Interiors
{
    // Canonical tag strings used to match FurnitureDefinitions against RoomDefinitions.
    // A room with the "bedroom" tag will accept any furniture whose tag set
    // contains "bedroom". "shared" is the universal pool that every room includes.
    public static class FurnitureTags
    {
        public const string Shared      = "shared";

        // Residential
        public const string Bedroom     = "bedroom";
        public const string Kitchen     = "kitchen";
        public const string LivingRoom  = "livingroom";
        public const string Bathroom    = "bathroom";
        public const string Dining      = "dining";
        public const string Hallway     = "hallway";
        public const string Basement    = "basement";

        // Office
        public const string Office      = "office";
        public const string Lobby       = "lobby";
        public const string Conference  = "conference";
        public const string Cubicle     = "cubicle";
        public const string BreakRoom   = "breakroom";
        public const string Server      = "server";

        // Factory
        public const string Factory     = "factory";
        public const string Workshop    = "workshop";
        public const string Storage     = "storage";
        public const string LoadingBay  = "loadingbay";
        public const string Locker      = "locker";
        public const string Cafeteria   = "cafeteria";
        public const string Power       = "power";
        public const string Control     = "control";

        // Cross-cutting
        public const string Utility     = "utility";   // bathrooms, storage closets
        public const string Generic     = "generic";   // generic boxy rooms
    }
}
