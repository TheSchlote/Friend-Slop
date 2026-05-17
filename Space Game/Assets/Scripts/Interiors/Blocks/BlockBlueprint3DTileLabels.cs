using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Floating world-space text labels above every Floor tile in the current
    // blueprint, shown only while the block editor is open. Uses TextMesh on
    // a child GameObject parented under the bootstrapper's interior root so
    // labels get destroyed naturally when the interior is rebuilt.
    public class BlockBlueprint3DTileLabels
    {
        private readonly BlockBlueprint3DEditor _editor;
        private readonly List<GameObject> _labels = new();

        public BlockBlueprint3DTileLabels(BlockBlueprint3DEditor editor)
        {
            _editor = editor;
        }

        public void Clear()
        {
            foreach (var go in _labels) if (go != null) Object.Destroy(go);
            _labels.Clear();
        }

        public void Refresh()
        {
            Clear();
            if (_editor?.Bootstrapper == null) return;
            var root = _editor.Bootstrapper.InteriorRoomsRoot;
            if (root == null) return;
            var bp = _editor.Blueprint;
            if (bp == null) return;

            float c = bp.CellMetres;
            float h = bp.WallHeightMetres;

            foreach (var b in bp.Blocks)
            {
                if (b.Kind != BlockKind.Floor) continue;
                if (string.IsNullOrEmpty(b.Label)) continue;
                var go = new GameObject($"[TileLabel:{b.Cell.x}_{b.Cell.y}_{b.Cell.z}]");
                go.transform.SetParent(root, false);
                // Lay the text FLAT on the floor surface, just above it to
                // avoid z-fighting. Rotated 90° about X so the text plane is
                // horizontal and reads correctly looking down at the tile.
                go.transform.localPosition = new Vector3(
                    (b.Cell.x + 0.5f) * c,
                     b.Cell.y * h + 0.05f,
                    (b.Cell.z + 0.5f) * c);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var tm = go.AddComponent<TextMesh>();
                tm.text       = b.Label;
                tm.anchor     = TextAnchor.MiddleCenter;
                tm.alignment  = TextAlignment.Center;
                tm.fontSize   = 50;
                tm.characterSize = 0.04f;
                tm.color      = new Color(1f, 0.95f, 0.5f, 1f);
                _labels.Add(go);
            }
        }
    }
}
