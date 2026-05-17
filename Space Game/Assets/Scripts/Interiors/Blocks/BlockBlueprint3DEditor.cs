using System;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FriendSlop.Interiors.Blocks
{
    // Replacement F3 editor for the residential block system. Lives only when
    // the player is inside a Building_Interior loaded from a BlockBlueprint
    // (i.e. the bootstrapper has IsBlockMode == true). Auto-closes if the
    // interior unloads.
    //
    // Pauses Time.timeScale = 0 to freeze monsters/AI but flips
    // NetworkFirstPersonController.UseUnscaledOwnerTime so the player can still
    // walk and look while editing.
    public class BlockBlueprint3DEditor : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.F3;

        public bool IsActive { get; private set; }
        public static bool IsAnyActive => s_instance != null && s_instance.IsActive;
        private static BlockBlueprint3DEditor s_instance;

        // Editor state (mutable by the input handler + palette UI).
        public BlockBlueprintAsset Blueprint { get; private set; }
        public BlockPrefabCatalog Catalog { get; private set; }
        public InteriorSceneBootstrapper Bootstrapper { get; private set; }
        public BlockKind SelectedKind = BlockKind.Floor;
        // When true, scroll wheel landed on the "Delete" slot — LMB removes
        // the block under the cursor instead of placing a new one. The ghost
        // turns into a red highlight on the targeted block.
        public bool DeleteMode;
        // Paint mode: LMB on a wall/door stamps PaintPrefabName + PaintColorIndex
        // onto the side you're facing (per-wall override). Doesn't place/delete.
        public bool PaintMode;
        public string PaintPrefabName = "";   // empty = keep room variant, colour only
        public int    PaintColorIndex = 0;
        public string StyleTag = "";
        public string TagsString = "bedroom";
        public string Label = "Bedroom";
        public int EditFloor;
        public int Rotation;

        private BlockBlueprint3DCursor _cursor;
        private BlockBlueprint3DGhost _ghost;
        private BlockBlueprint3DPaletteUI _palette;
        private BlockBlueprint3DInputHandler _input;
        private BlockBlueprint3DTileLabels _labels;
        private BlockBlueprint3DCursor.Result _lastResult;

        private float _previousTimeScale = 1f;
        private CursorLockMode _previousLockState;
        private bool _previousCursorVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindFirstObjectByType<BlockBlueprint3DEditor>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("[BlockBlueprint3DEditor]");
            DontDestroyOnLoad(go);
            go.AddComponent<BlockBlueprint3DEditor>();
            Debug.Log("[BlockBlueprint3DEditor] Auto-spawned. Press F3 inside a block-blueprint interior to edit.");
        }

        private void OnEnable()  => s_instance = this;
        private void OnDisable() { if (s_instance == this) s_instance = null; if (IsActive) Close(); }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool togglePressed = kb[toggleKey].wasPressedThisFrame || kb.f3Key.wasPressedThisFrame;
            if (togglePressed) Toggle();
            if (!IsActive) return;

            // Auto-close if the interior scene unloaded under us.
            if (Bootstrapper == null || Blueprint == null)
            {
                Debug.Log("[BlockBlueprint3DEditor] Interior gone — closing.");
                Close();
                return;
            }

            // Cursor policy — locked by default, Alt to free for UI.
            bool freeCursor = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            var desiredLock = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.lockState != desiredLock) Cursor.lockState = desiredLock;
            if (Cursor.visible != freeCursor) Cursor.visible = freeCursor;

            _cursor.EditFloor = EditFloor;
            // Delete + Paint both need "find any block under cursor" targeting.
            _lastResult = _cursor.Tick(Blueprint, SelectedKind, DeleteMode || PaintMode);

            var mouse = Mouse.current;
            bool mouseOverUI = false;
            if (mouse != null && Cursor.lockState == CursorLockMode.None)
            {
                var mp = mouse.position.ReadValue();
                var guiMouse = new Vector2(mp.x, Screen.height - mp.y);
                mouseOverUI = _palette.ContainsScreenPoint(guiMouse);
            }

            bool edited = _input.Tick(_lastResult, suppressMouseActions: mouseOverUI);
            UpdateGhostPreview();
            if (edited)
            {
                // Server-only regen of the materialised interior.
                if (Bootstrapper != null && Bootstrapper.IsServer)
                    Bootstrapper.RegenerateFromBlockBlueprintFast();
                _labels?.Refresh();
            }
        }

        public void Toggle()
        {
            Debug.Log($"[BlockBlueprint3DEditor] Toggle requested. Active={IsActive}");
            if (IsActive) { Close(); return; }
            if (!CanOpen()) return;
            Open();
            Debug.Log("[BlockBlueprint3DEditor] Opened.");
        }

        private bool CanOpen()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && nm.ConnectedClientsIds.Count > 1)
            {
                Debug.Log("[BlockBlueprint3DEditor] Multiplayer session active — refusing to open. Solo/host-only dev tool.");
                return false;
            }
            Bootstrapper = FindFirstObjectByType<InteriorSceneBootstrapper>(FindObjectsInactive.Include);
            if (Bootstrapper == null || !Bootstrapper.IsBlockMode)
            {
                Debug.LogWarning("[BlockBlueprint3DEditor] No block-mode interior in scene. Walk into a residential building first.");
                return false;
            }
            Blueprint = InteriorSessionData.BlockBlueprint;
            if (Blueprint == null)
            {
                Debug.LogWarning("[BlockBlueprint3DEditor] No BlockBlueprint in session data.");
                return false;
            }
            Catalog = Bootstrapper.CurrentDefinition != null
                ? Bootstrapper.CurrentDefinition.BlockCatalog
                : null;
            if (Catalog == null)
            {
                Debug.LogWarning("[BlockBlueprint3DEditor] No BlockPrefabCatalog wired on the BuildingDefinition.");
                return false;
            }
            return true;
        }

        private void Open()
        {
            IsActive = true;
            _previousTimeScale     = Time.timeScale;
            _previousLockState     = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Time.timeScale = 0f;
            NetworkFirstPersonController.UseUnscaledOwnerTime = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            // Lambdas re-resolve the bootstrapper's current InteriorRoomsRoot
            // each call — RegenerateFromBlockBlueprintFast destroys + recreates
            // it on every edit, so a captured Transform would go stale.
            System.Func<Transform> rootGetter = () =>
                Bootstrapper != null ? Bootstrapper.InteriorRoomsRoot : null;
            _cursor  = new BlockBlueprint3DCursor(rootGetter);
            _ghost   = new BlockBlueprint3DGhost(rootGetter);
            _input   = new BlockBlueprint3DInputHandler(this);
            _palette = new BlockBlueprint3DPaletteUI(this);
            _labels  = new BlockBlueprint3DTileLabels(this);
            _labels.Refresh();

            // If the player was mid-fall when they pressed F3, immediately yank
            // them up to a safe spot so they can start placing without
            // re-entering the void on the next frame.
            if (Bootstrapper.IsServer) Bootstrapper.RescueNow();
        }

        private void Close()
        {
            IsActive = false;
            _ghost?.Clear();
            _labels?.Clear();
            _cursor = null; _ghost = null; _input = null; _palette = null; _labels = null;

            NetworkFirstPersonController.UseUnscaledOwnerTime = false;
            Time.timeScale   = _previousTimeScale;
            Cursor.lockState = _previousLockState;
            Cursor.visible   = _previousCursorVisible;

            // Persist the asset so changes survive play-mode exit.
            #if UNITY_EDITOR
            if (Blueprint != null) { EditorUtility.SetDirty(Blueprint); AssetDatabase.SaveAssets(); }
            #endif

            Bootstrapper = null;
            Blueprint    = null;
            Catalog      = null;
        }

        private void OnGUI()
        {
            if (!IsActive || _palette == null) return;
            _palette.OnGUI();
            DrawCrosshair();
        }

        // ── Public API for the input handler ────────────────────────────────

        // Switch the live blueprint (Load / New from the sidebar). Updates the
        // session + building def via BlockBlueprintIO, then refreshes the
        // editor's own state + tile labels.
        public void SetBlueprint(BlockBlueprintAsset bp)
        {
            if (bp == null) return;
            Blueprint = bp;
            BlockBlueprintIO.SetActive(bp, Bootstrapper);
            _labels?.Refresh();
        }

        // Scroll cycle: every BlockKind, then Delete, then Paint. Exactly one
        // of {a kind, Delete, Paint} is active at a time.
        public void CycleSelectedKind(int direction)
        {
            var vals = (BlockKind[])Enum.GetValues(typeof(BlockKind));
            int kindCount = vals.Length;
            int total     = kindCount + 2;          // + Delete + Paint
            int currentIdx = PaintMode  ? kindCount + 1
                           : DeleteMode ? kindCount
                           : Array.IndexOf(vals, SelectedKind);
            int next = ((currentIdx + direction) % total + total) % total;
            DeleteMode = next == kindCount;
            PaintMode  = next == kindCount + 1;
            if (!DeleteMode && !PaintMode) SelectedKind = vals[next];
        }

        // Stamp the paint selection onto the wall/door under the cursor, on the
        // side the player is facing. Returns true if a wall was painted.
        public bool PaintWallUnderCursor(in BlockBlueprint3DCursor.Result cursor)
        {
            if (cursor.ExistingIndex < 0 || cursor.ExistingIndex >= Blueprint.Blocks.Count)
                return false;
            var b = Blueprint.Blocks[cursor.ExistingIndex];
            if (b.Kind != BlockKind.Wall && b.Kind != BlockKind.Door) return false;
            // "Front" = the side facing the wall's own Cell. The cursor's Cell
            // is the cell the player aimed from; if it matches the wall's Cell
            // they're on the front side, else the back.
            bool front = cursor.Cell == b.Cell;
            if (front)
            {
                b.OverrideFront   = true;
                b.FrontPrefabName = PaintPrefabName;
                b.FrontColorIndex = PaintColorIndex;
            }
            else
            {
                b.OverrideBack   = true;
                b.BackPrefabName = PaintPrefabName;
                b.BackColorIndex = PaintColorIndex;
            }
            Blueprint.Blocks[cursor.ExistingIndex] = b;
            Debug.Log($"[Block3D] Painted {(front ? "front" : "back")} of {b.Kind} at {b.Cell} → '{PaintPrefabName}' colour {PaintColorIndex}.");
            return true;
        }

        // Clear the per-wall override on the side the player faces — that side
        // falls back to its room's RoomStyle again.
        public bool ClearPaintUnderCursor(in BlockBlueprint3DCursor.Result cursor)
        {
            if (cursor.ExistingIndex < 0 || cursor.ExistingIndex >= Blueprint.Blocks.Count)
                return false;
            var b = Blueprint.Blocks[cursor.ExistingIndex];
            if (b.Kind != BlockKind.Wall && b.Kind != BlockKind.Door) return false;
            bool front = cursor.Cell == b.Cell;
            if (front) b.OverrideFront = false;
            else       b.OverrideBack  = false;
            Blueprint.Blocks[cursor.ExistingIndex] = b;
            Debug.Log($"[Block3D] Cleared {(front ? "front" : "back")} override on {b.Kind} at {b.Cell}.");
            return true;
        }

        public BlockEntry MakeEntryFromCursor(in BlockBlueprint3DCursor.Result cursor)
        {
            var e = new BlockEntry
            {
                Cell     = new Vector3Int(cursor.Cell.x, EditFloor, cursor.Cell.z),
                Kind     = SelectedKind,
                Edge     = cursor.Edge,
                Rotation = Rotation,
                StyleTag = StyleTag,
            };
            if (SelectedKind == BlockKind.Floor)
            {
                e.Tags  = ParseTags(TagsString);
                e.Label = Label;
            }
            return e;
        }

        public void PlaceBlock(BlockEntry entry)
        {
            Blueprint.Blocks.Add(entry);
            Debug.Log($"[Block3D] Placed {entry.Kind} at cell {entry.Cell} edge {entry.Edge}.");
        }

        public void DeleteBlockAt(int index)
        {
            if (index < 0 || index >= Blueprint.Blocks.Count) return;
            var b = Blueprint.Blocks[index];
            Blueprint.Blocks.RemoveAt(index);
            Debug.Log($"[Block3D] Deleted {b.Kind} at cell {b.Cell} edge {b.Edge}.");
        }

        private static string[] ParseTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<string>();
            var parts = s.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        // Update the ghost preview to match current cursor + kind + rotation.
        private void UpdateGhostPreview()
        {
            if (!_lastResult.HasTarget) { _ghost.Clear(); return; }

            // Paint mode: highlight the wall/door under the cursor so the
            // player can see what they'll re-skin. Uses the same red-ish
            // highlight as delete (conflict=true) — it's just a "selected"
            // indicator.
            if (PaintMode)
            {
                if (_lastResult.ExistingIndex < 0
                    || _lastResult.ExistingIndex >= Blueprint.Blocks.Count)
                {
                    _ghost.Clear();
                    return;
                }
                var pe = Blueprint.Blocks[_lastResult.ExistingIndex];
                if (pe.Kind != BlockKind.Wall && pe.Kind != BlockKind.Door)
                {
                    _ghost.Clear();
                    return;
                }
                _ghost.SetTarget(Blueprint, Catalog, pe.Kind, pe.Cell, pe.Edge,
                                  pe.StyleTag, pe.Rotation, conflict: true, label: pe.Label);
                return;
            }

            // Delete mode: red-tinted highlight on the existing block under
            // the cursor (if any) instead of a placement preview.
            if (DeleteMode)
            {
                if (_lastResult.ExistingIndex < 0
                    || _lastResult.ExistingIndex >= Blueprint.Blocks.Count)
                {
                    _ghost.Clear();
                    return;
                }
                var existing = Blueprint.Blocks[_lastResult.ExistingIndex];
                if (existing.Kind == BlockKind.FloorHole || existing.Kind == BlockKind.Ceiling)
                {
                    _ghost.Clear();
                    return;
                }
                // conflict=true makes the ghost render red, perfect for "about
                // to delete". Re-uses the existing block's own kind / cell /
                // edge / style so the highlight covers exactly what gets nuked.
                _ghost.SetTarget(Blueprint, Catalog, existing.Kind, existing.Cell,
                                  existing.Edge, existing.StyleTag, existing.Rotation,
                                  conflict: true, label: existing.Label);
                return;
            }

            // FloorHole / Ceiling don't have visuals — skip ghost for them.
            if (SelectedKind == BlockKind.FloorHole || SelectedKind == BlockKind.Ceiling)
            {
                _ghost.Clear();
                return;
            }
            var cell = new Vector3Int(_lastResult.Cell.x, EditFloor, _lastResult.Cell.z);
            // Pass the same Label the editor would write when placing this
            // block so the ghost picks the same room-grouped variant as the
            // eventual spawn (for Floor tiles only — walls/ceiling resolve
            // their room key from neighbouring Floor tiles automatically).
            string ghostLabel = SelectedKind == BlockKind.Floor ? Label : null;
            _ghost.SetTarget(Blueprint, Catalog, SelectedKind, cell, _lastResult.Edge,
                              StyleTag, Rotation, _lastResult.Conflict, ghostLabel);
        }

        private static Texture2D _crosshairTex;
        private static void DrawCrosshair()
        {
            if (_crosshairTex == null)
            {
                _crosshairTex = new Texture2D(1, 1);
                _crosshairTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.9f));
                _crosshairTex.Apply();
            }
            const float s = 10f;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - s, Screen.height / 2f - 1f, s * 2f, 2f), _crosshairTex);
            GUI.DrawTexture(new Rect(Screen.width / 2f - 1f, Screen.height / 2f - s, 2f, s * 2f), _crosshairTex);
            GUI.color = prev;
        }
    }
}
