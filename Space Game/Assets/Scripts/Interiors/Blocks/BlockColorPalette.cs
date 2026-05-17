using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // 24-swatch preset palette for per-room / per-wall tinting. Index -1 means
    // "None" — no tint, the raw material renders. Index 0..23 maps into Presets.
    public static class BlockColorPalette
    {
        public const int None = -1;

        // 24 readable interior-friendly hues (warm + cool neutrals + accents).
        public static readonly Color[] Presets =
        {
            new(0.86f, 0.86f, 0.84f), // off-white
            new(0.74f, 0.71f, 0.64f), // beige
            new(0.60f, 0.55f, 0.47f), // taupe
            new(0.45f, 0.40f, 0.34f), // dark taupe
            new(0.78f, 0.66f, 0.52f), // tan
            new(0.62f, 0.46f, 0.32f), // wood brown
            new(0.40f, 0.28f, 0.20f), // dark wood
            new(0.86f, 0.55f, 0.42f), // terracotta
            new(0.80f, 0.32f, 0.30f), // brick red
            new(0.55f, 0.20f, 0.22f), // deep red
            new(0.88f, 0.74f, 0.42f), // mustard
            new(0.92f, 0.85f, 0.55f), // pale yellow
            new(0.55f, 0.68f, 0.45f), // sage green
            new(0.34f, 0.52f, 0.38f), // forest green
            new(0.42f, 0.62f, 0.66f), // teal
            new(0.55f, 0.72f, 0.85f), // sky blue
            new(0.34f, 0.45f, 0.66f), // slate blue
            new(0.26f, 0.30f, 0.45f), // navy
            new(0.62f, 0.55f, 0.70f), // lavender
            new(0.45f, 0.36f, 0.52f), // plum
            new(0.80f, 0.62f, 0.70f), // dusty pink
            new(0.55f, 0.55f, 0.58f), // mid grey
            new(0.36f, 0.37f, 0.40f), // charcoal
            new(0.12f, 0.13f, 0.15f), // near-black
        };

        public static bool TryGet(int index, out Color color)
        {
            if (index >= 0 && index < Presets.Length) { color = Presets[index]; return true; }
            color = Color.white;
            return false;   // None
        }
    }
}
