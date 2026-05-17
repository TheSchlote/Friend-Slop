using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Interiors.Blocks
{
    // Translates keyboard + mouse input into block-editor operations. The editor
    // owns all state; this class just reads input and pokes editor methods.
    public class BlockBlueprint3DInputHandler
    {
        private readonly BlockBlueprint3DEditor _editor;
        public BlockBlueprint3DInputHandler(BlockBlueprint3DEditor editor) { _editor = editor; }

        // Returns true if an edit was applied this frame so the editor can
        // refresh dependent visualisations (tile labels, etc.).
        public bool Tick(in BlockBlueprint3DCursor.Result cursor, bool suppressMouseActions)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return false;

            // Edit-floor switching with PageUp / PageDown — also bracket keys
            // as a fallback for keyboards that don't have PageUp on the home
            // cluster.
            if (kb.pageUpKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame)
            {
                _editor.EditFloor++;
                Debug.Log($"[Block3D] Edit floor → {_editor.EditFloor}");
            }
            if (kb.pageDownKey.wasPressedThisFrame || kb.leftBracketKey.wasPressedThisFrame)
            {
                _editor.EditFloor--;
                Debug.Log($"[Block3D] Edit floor → {_editor.EditFloor}");
            }

            // Q / E rotate the block being placed (matters for stairs + door
            // visuals + floor tile orientation).
            if (kb.qKey.wasPressedThisFrame) _editor.Rotation = ((_editor.Rotation - 1) % 4 + 4) % 4;
            if (kb.eKey.wasPressedThisFrame) _editor.Rotation = (_editor.Rotation + 1) % 4;

            // Scroll cycles selected block kind.
            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
                _editor.CycleSelectedKind(scrollY > 0f ? -1 : +1);

            if (suppressMouseActions || !cursor.HasTarget) return false;

            bool edited = false;
            // Paint mode: LMB stamps the paint selection onto the wall/door
            // side the player faces. RMB clears that side's override.
            if (_editor.PaintMode)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (_editor.PaintWallUnderCursor(cursor)) edited = true;
                    else Debug.Log("[Block3D] Paint ignored: aim at a wall or door.");
                }
                else if (mouse.rightButton.wasPressedThisFrame)
                {
                    if (_editor.ClearPaintUnderCursor(cursor)) edited = true;
                    else Debug.Log("[Block3D] Clear-paint ignored: aim at a wall or door.");
                }
            }
            // Delete mode: LMB and RMB both nuke the block under the cursor.
            // Otherwise normal place/delete split.
            else if (_editor.DeleteMode)
            {
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    if (cursor.ExistingIndex < 0)
                        Debug.Log("[Block3D] Delete ignored: no block under cursor.");
                    else
                    {
                        _editor.DeleteBlockAt(cursor.ExistingIndex);
                        edited = true;
                    }
                }
            }
            else
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (cursor.Conflict)
                    {
                        Debug.Log($"[Block3D] LMB ignored: a {_editor.SelectedKind} block already exists at {cursor.Cell}/{cursor.Edge}.");
                    }
                    else
                    {
                        var entry = _editor.MakeEntryFromCursor(cursor);
                        _editor.PlaceBlock(entry);
                        edited = true;
                    }
                }
                else if (mouse.rightButton.wasPressedThisFrame)
                {
                    // RMB in place mode rotates the about-to-place block 90°
                    // CW. To delete, switch to Delete via the scroll wheel.
                    _editor.Rotation = (_editor.Rotation + 1) % 4;
                    Debug.Log($"[Block3D] Rotation → {_editor.Rotation * 90}°");
                }
            }
            return edited;
        }
    }
}
