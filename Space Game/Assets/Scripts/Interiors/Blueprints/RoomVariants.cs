using System.Collections.Generic;

namespace FriendSlop.Interiors.Blueprints
{
    // Family-based variant grouping for RoomDefinitions. Two defs are variants
    // of each other if they share the same "family name" — the asset name with
    // any single-character ".A"/".B"/".0" etc. suffix stripped. e.g.:
    //   Room_Residential_Bathroom_2x2     → family Room_Residential_Bathroom_2x2
    //   Room_Residential_Bathroom_2x2.A   → family Room_Residential_Bathroom_2x2
    //   Room_Residential_Bathroom_2x2.B   → family Room_Residential_Bathroom_2x2
    //
    // The blueprint stores a reference to one specific def. At spawn time the
    // BlueprintLayoutBuilder looks up the family in the building's RoomPool
    // and picks a random variant (System.Random), giving spawn-time variety
    // without changing the blueprint.
    public static class RoomVariants
    {
        // Family name = name with single-char alphanumeric .X suffix stripped.
        public static string GetFamilyName(string roomName)
        {
            if (string.IsNullOrEmpty(roomName)) return roomName;
            int dot = roomName.LastIndexOf('.');
            if (dot < 1 || dot >= roomName.Length - 1) return roomName;
            string suffix = roomName.Substring(dot + 1);
            if (suffix.Length != 1) return roomName;
            char c = suffix[0];
            if (!(char.IsLetterOrDigit(c))) return roomName;
            return roomName.Substring(0, dot);
        }

        // Returns every def in `pool` that shares `def`'s family AND grid size.
        // Grid-size match is required because variants are picked at spawn time
        // and substituted into the same blueprint slot — a different size would
        // overlap the next room or leave a gap. Falls back to a single-item
        // list with `def` if no matches.
        public static List<RoomDefinition> FindVariants(RoomDefinition def,
            System.Collections.Generic.IReadOnlyList<RoomDefinition> pool)
        {
            var variants = new List<RoomDefinition>();
            if (def == null) return variants;
            string family = GetFamilyName(def.name);
            if (pool != null)
            {
                foreach (var d in pool)
                {
                    if (d == null) continue;
                    if (GetFamilyName(d.name) != family) continue;
                    if (d.GridSize != def.GridSize) continue;
                    variants.Add(d);
                }
            }
            if (variants.Count == 0) variants.Add(def);
            return variants;
        }
    }
}
