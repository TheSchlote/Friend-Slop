using UnityEngine;

namespace FriendSlop.Interiors
{
    // Static data carrier set by InteriorEntrance before loading the interior scene.
    // Lives in the host process only — safe for this game's listen-server model.
    public static class InteriorSessionData
    {
        // Interior rooms are generated at this world position, far above the planet
        // so SphereWorld gravity points straight down (+Y away from center).
        // A FlatGravityVolume in the interior scene overrides gravity precisely.
        public static readonly Vector3 InteriorWorldOrigin = new Vector3(0f, 2000f, 0f);

        public static int Seed;
        public static BuildingDefinition Definition;
        public static Vector3 ReturnPosition;
        public static Quaternion ReturnRotation;
        public static ulong RequestingClientId;
        public static string ScenePath;
        // When non-null, the bootstrapper materialises this blueprint instead of
        // running the procedural generator. Definition is still used for cell size,
        // theme colour, etc. Set by BlueprintEntrance before scene load.
        public static FriendSlop.Interiors.Blueprints.BlueprintAsset Blueprint;
    }
}
