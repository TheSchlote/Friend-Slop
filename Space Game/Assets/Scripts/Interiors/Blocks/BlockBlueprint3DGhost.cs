using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Translucent preview of the block about to be placed, anchored to the
    // editor's root transform so it follows regen / rebuild. Re-spawns the
    // prefab only when the target changes, not every frame.
    public class BlockBlueprint3DGhost
    {
        private readonly System.Func<Transform> _rootGetter;
        private GameObject _instance;
        private BlockKind _kind;
        private Vector3Int _cell;
        private SocketDirection _edge;
        private bool _conflict;

        private static readonly Color ValidTint   = new(0.35f, 0.65f, 1.00f, 0.55f);
        private static readonly Color BlockedTint = new(1.00f, 0.30f, 0.30f, 0.55f);
        private static readonly int BaseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId       = Shader.PropertyToID("_Color");

        public BlockBlueprint3DGhost(System.Func<Transform> rootGetter)
        {
            _rootGetter = rootGetter;
        }

        public void Clear()
        {
            if (_instance != null) Object.Destroy(_instance);
            _instance = null;
        }

        public void SetTarget(BlockBlueprintAsset bp, BlockPrefabCatalog catalog,
                               BlockKind kind, Vector3Int cell, SocketDirection edge,
                               string styleTag, int rotation, bool conflict,
                               string label = null)
        {
            var root = _rootGetter?.Invoke();
            if (bp == null || catalog == null || root == null) { Clear(); return; }
            bool changed = _instance == null || kind != _kind || cell != _cell
                            || edge != _edge;
            if (changed)
            {
                Clear();
                _kind = kind; _cell = cell; _edge = edge;
                // The stub needs the SAME Label / Tags the editor will write
                // when the block is actually placed, so the ghost ends up in
                // the same room group as the placed block. The editor passes
                // those in via BlockBlueprint3DEditor.MakeEntryFromCursor when
                // we call SetTarget below — we replicate that logic here.
                var stub = new BlockEntry { Cell = cell, Kind = kind, Edge = edge,
                                             Rotation = rotation, StyleTag = styleTag,
                                             Label = label };
                // Use the same deterministic seed the materialiser will use when
                // this block is actually placed, so the preview matches output.
                var variant = catalog.Pick(kind, styleTag,
                    BlockBlueprintMaterializer.VariantSeed(bp, stub));
                if (variant == null || variant.Prefab == null) return;
                if (!BlockBlueprintMaterializer.ComputeTransform(stub,
                        bp.CellMetres, bp.WallHeightMetres, out var pos, out var rot))
                    return;
                // Mirror the materialiser's wrapper+visual structure exactly so
                // the ghost is the same size as the eventual placed block:
                // wrapper at pos/rot, prefab as child, same FitVisualToCell.
                _instance = new GameObject($"[Ghost:{kind}]");
                _instance.transform.SetParent(root, false);
                _instance.transform.localPosition = pos;
                _instance.transform.localRotation = rot;
                var visual = Object.Instantiate(variant.Prefab, _instance.transform, false);
                foreach (var col in visual.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
                BlockBlueprintMaterializer.FitVisualToCell(
                    visual, kind, bp.CellMetres, bp.WallHeightMetres);
            }
            if (_conflict != conflict || changed)
            {
                _conflict = conflict;
                ApplyTint();
            }
        }

        private void ApplyTint()
        {
            if (_instance == null) return;
            var mpb = new MaterialPropertyBlock();
            var tint = _conflict ? BlockedTint : ValidTint;
            mpb.SetColor(BaseColorId, tint);
            mpb.SetColor(ColorId,     tint);
            foreach (var r in _instance.GetComponentsInChildren<MeshRenderer>(true))
                r.SetPropertyBlock(mpb);
        }
    }
}
