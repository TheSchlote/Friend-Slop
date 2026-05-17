using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Catalog mapping BlockKind → a pool of prefab variants. Each variant can
    // declare a StyleTag so the materialiser can pick a matching variant for
    // a given placed block. Variants without a StyleTag are picked when the
    // block has no style preference.
    //
    // Auto-populated by the "Repair Block Catalog" editor menu — designers
    // don't wire these by hand. Authored variants persist through re-scans
    // (existing entries keep their StyleTag overrides).
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Block Prefab Catalog",
                     fileName = "BlockPrefabCatalog")]
    public class BlockPrefabCatalog : ScriptableObject
    {
        [Serializable]
        public class Variant
        {
            public GameObject Prefab;
            public BlockKind  Kind;
            // Free-form style tag (e.g. "smooth", "brick", "painted"). Matches
            // against BlockEntry.StyleTag — leave empty for "any style".
            public string     StyleTag;
        }

        public List<Variant> Variants = new();

        // Pick a variant matching `kind` and (optionally) `styleTag`. If no
        // variants match the style, falls back to any variant of the kind.
        // Returns null only when no variants of the kind exist at all.
        //
        // Pass the same `seed` for a stable choice — used by the materialiser
        // and the editor ghost so the preview matches what eventually spawns.
        // Default (null) gives a fresh random pick per call.
        public Variant Pick(BlockKind kind, string styleTag, int? seed = null)
        {
            var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            // Two-pass: prefer matching style, fall back to any-style.
            int styled = 0;
            foreach (var v in Variants)
                if (v != null && v.Kind == kind && MatchStyle(v.StyleTag, styleTag)) styled++;
            if (styled > 0)
            {
                int pick = rng.Next(styled);
                foreach (var v in Variants)
                {
                    if (v == null || v.Kind != kind) continue;
                    if (!MatchStyle(v.StyleTag, styleTag)) continue;
                    if (pick-- == 0) return v;
                }
            }
            int any = 0;
            foreach (var v in Variants)
                if (v != null && v.Kind == kind) any++;
            if (any == 0) return null;
            int pickAny = rng.Next(any);
            foreach (var v in Variants)
            {
                if (v == null || v.Kind != kind) continue;
                if (pickAny-- == 0) return v;
            }
            return null;
        }

        // Exact-prefab lookup for explicit per-room / per-wall variant choice.
        // Returns null when no variant of that kind has a prefab named `name`.
        public Variant GetByName(BlockKind kind, string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            foreach (var v in Variants)
                if (v != null && v.Kind == kind && v.Prefab != null
                    && v.Prefab.name == prefabName) return v;
            return null;
        }

        // Distinct prefab names of a kind, for the sidebar dropdown.
        public System.Collections.Generic.List<string> NamesOfKind(BlockKind kind)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var v in Variants)
            {
                if (v == null || v.Kind != kind || v.Prefab == null) continue;
                if (!list.Contains(v.Prefab.name)) list.Add(v.Prefab.name);
            }
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        // Variant's style matches the requested one. Empty/null requested style
        // matches anything; empty variant style matches anything; otherwise
        // exact (case-insensitive) match.
        private static bool MatchStyle(string variantStyle, string requested)
        {
            if (string.IsNullOrEmpty(requested)) return true;
            if (string.IsNullOrEmpty(variantStyle)) return true;
            return string.Equals(variantStyle, requested, StringComparison.OrdinalIgnoreCase);
        }
    }
}
