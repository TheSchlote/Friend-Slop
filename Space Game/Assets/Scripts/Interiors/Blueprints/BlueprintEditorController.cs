using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FriendSlop.Interiors.Blueprints
{
    // Runtime owner of the blueprint editor state. Toggled with F1 in playtest;
    // when active, freezes gameplay (Time.timeScale = 0) and swaps to a 2D
    // top-down view rendered by BlueprintEditorUI. Uses UnityEditor.AssetDatabase
    // for save/load — only meaningful in editor Play Mode (which is the intended
    // workflow per CLAUDE.md's vibe-coded note).
    public class BlueprintEditorController : MonoBehaviour
    {
        public const string BlueprintFolder = "Assets/Interiors/Blueprints";

        [SerializeField] private Key toggleKey  = Key.F1;
        [SerializeField] private Key refreshKey = Key.F2;

        // Spawn ourselves on first scene load so the editor is always reachable in
        // play mode without manual scene setup. Idempotent — won't double-spawn.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindFirstObjectByType<BlueprintEditorController>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("[BlueprintEditor]");
            DontDestroyOnLoad(go);
            go.AddComponent<BlueprintEditorController>();
            go.AddComponent<BlueprintEditorUI>();
        }

        public bool IsActive { get; private set; }
        // True when the 2D editor pane is up — drives FriendSlopUI's cursor/input gating.
        public static bool IsBlockingInput =>
            s_instance != null && s_instance.IsActive;
        // GameObject root for the live test building near the launchpad. Persists
        // across editor open/close so the user can walk to it without the IMGUI
        // overlay in the way. Re-spawned on blueprint load, edits, and saves.
        private Transform _testBuildingRoot;
        private BlueprintAsset _spawnedFor;  // last blueprint we spawned for; null = none

        // Static accessor so other systems (FriendSlopUI cursor logic, the player FPS
        // controller's click-to-lock check) can ask "is the dev editor open?" without
        // a hard reference. Returns true if any spawned BlueprintEditorController is
        // currently active.
        private static BlueprintEditorController s_instance;
        public static bool IsAnyActive => s_instance != null && s_instance.IsActive;
        private void OnEnable()  => s_instance = this;
        private void OnDisable() { if (s_instance == this) s_instance = null; }

        // The blueprint currently being edited. Null until New or Load is invoked.
        public BlueprintAsset Current { get; private set; }

        // Currently-selected room definition from the palette. Determines what gets
        // placed when the user clicks an empty grid cell.
        public RoomDefinition SelectedRoomDef { get; set; }

        // Index into Current.Rooms of the placement currently being inspected/edited
        // in the right-side definition pane. -1 means nothing selected. Drives the
        // Phase 2 inspector UI.
        public int SelectedPlacementIndex { get; set; } = -1;
        public RoomPlacement? SelectedPlacement
        {
            get
            {
                if (Current == null) return null;
                if (SelectedPlacementIndex < 0 || SelectedPlacementIndex >= Current.Rooms.Count) return null;
                return Current.Rooms[SelectedPlacementIndex];
            }
        }
        // Standalone def selection — set by the Browse picker so users can edit
        // defs that aren't currently placed. Takes precedence over SelectedPlacement
        // when set.
        public RoomDefinition SelectedStandaloneDef { get; set; }
        public RoomDefinition SelectedDefinition
        {
            get
            {
                if (SelectedStandaloneDef != null) return SelectedStandaloneDef;
                var p = SelectedPlacement;
                return p?.Definition;
            }
        }

        // Cached room palette, populated on first Toggle. AssetDatabase scan only
        // works in editor — we guard access elsewhere.
        public IReadOnlyList<RoomDefinition> Palette => _palette;
        private readonly List<RoomDefinition> _palette = new();

        // Cached blueprint list for the load picker.
        public IReadOnlyList<BlueprintAsset> SavedBlueprints => _savedBlueprints;
        private readonly List<BlueprintAsset> _savedBlueprints = new();

        private float _previousTimeScale = 1f;

        // Placement-rotation state, advanced by Q/E hotkeys (also by toolbar buttons).
        // Kept here rather than in the UI so the same value persists across UI redraws.
        public int PlaceRotation { get; private set; }
        public void RotateCcw() => PlaceRotation = (PlaceRotation + 3) & 3;
        public void RotateCw()  => PlaceRotation = (PlaceRotation + 1) & 3;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb[toggleKey].wasPressedThisFrame) Toggle();
            if (!IsActive) return;
            if (kb[refreshKey].wasPressedThisFrame) RefreshTestBuilding();
            // Keep cursor unlocked while editor is up. The player FPS controller
            // re-locks every frame; we override every frame.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            if (kb[Key.Q].wasPressedThisFrame) RotateCcw();
            if (kb[Key.E].wasPressedThisFrame) RotateCw();
        }

        public void Toggle()
        {
            IsActive = !IsActive;
            if (IsActive)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale     = 0f;
                Cursor.lockState   = CursorLockMode.None;
                Cursor.visible     = true;
                RefreshPalette();
                RefreshSavedBlueprints();
                RefreshTestBuilding();
            }
            else
            {
                Time.timeScale = _previousTimeScale;
                // Test building intentionally persists so the user can close the
                // editor and walk into it without the IMGUI overlay in the way.
            }
        }

        // Cell metres + floor height used by the test building. Should match the
        // BuildingDefinition that will eventually use this blueprint at runtime.
        private const float PreviewCellMetres   = 3.4f;
        private const float PreviewFloorHeight  = 4f;

        // Scene the test building is allowed to spawn in. Restricted so it doesn't
        // pollute other planet scenes the player might transition to during a
        // round. If you want the building elsewhere, change this constant.
        private const string TestBuildingSceneName = "Planet_HillsAndValleys";

        // Spawns or refreshes the live test building near the launchpad. Called
        // automatically after every blueprint mutation (place/delete/edge edit/
        // load/save) so the building always reflects what's in the editor.
        // Restricted to the Hills-and-Valleys test scene so other planets stay clean.
        public void RefreshTestBuilding()
        {
            if (Current == null) { ClearTestBuilding(); return; }
            // Wrong scene → nothing to do. Don't even clear an existing building
            // because the user might just be passing through a different planet.
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name != TestBuildingSceneName) return;

            // Prefer the persistent in-scene host (set up via Tools → ... →
            // Setup Test Building Host). It owns the build position and survives
            // play-mode exit. If it exists, route children there instead of
            // creating our own root.
            var host = Object.FindFirstObjectByType<BlueprintTestBuildingHost>(
                FindObjectsInactive.Include);
            if (host != null)
            {
                host.blueprint = Current;
                host.Refresh();
                return;
            }
            ClearTestBuilding();

            // Anchor the building near the launchpad on the planet surface so the
            // player can walk to it. Falls back to world origin if no launchpad.
            Vector3 anchorPos    = Vector3.zero;
            Quaternion anchorRot = Quaternion.identity;
            var launchpad = Object.FindFirstObjectByType<FriendSlop.Round.LaunchpadZone>();
            if (launchpad != null)
            {
                // 30 m to the right of the launchpad, with the launchpad's "up"
                // (away from the planet centre) becoming the building's local +Y.
                anchorPos = launchpad.transform.position + launchpad.transform.right * 30f;
                anchorRot = launchpad.transform.rotation;
            }

            _testBuildingRoot = new GameObject($"[BlueprintTest:{Current.DisplayName}]").transform;
            _testBuildingRoot.SetPositionAndRotation(anchorPos, anchorRot);
            _spawnedFor = Current;

            foreach (var room in Current.Rooms)
            {
                if (room.Definition == null || room.Definition.Prefab == null) continue;
                var size = room.Definition.GridSize;
                Vector3 localPos = new Vector3(
                    room.GridPosition.x * PreviewCellMetres,
                    room.GridPosition.y * PreviewFloorHeight,
                    room.GridPosition.z * PreviewCellMetres);
                // Rotation around the prefab's SW corner pivot shifts the room out
                // of the +X+Z quadrant. Compensate so the rotated room's SW corner
                // stays at its grid-position local coords.
                Vector3 rotShift = (room.Rotation & 3) switch
                {
                    1 => new Vector3(0f,                       0f, size.x * PreviewCellMetres),
                    2 => new Vector3(size.x * PreviewCellMetres, 0f, size.y * PreviewCellMetres),
                    3 => new Vector3(size.y * PreviewCellMetres, 0f, 0f),
                    _ => Vector3.zero,
                };
                Quaternion localRot = Quaternion.Euler(0f, room.Rotation * 90f, 0f);
                var go = Object.Instantiate(room.Definition.Prefab, _testBuildingRoot);
                go.transform.localPosition = localPos + rotShift;
                go.transform.localRotation = localRot;
            }
        }

        public void ClearTestBuilding()
        {
            if (_testBuildingRoot != null)
            {
                Object.Destroy(_testBuildingRoot.gameObject);
                _testBuildingRoot = null;
            }
            _spawnedFor = null;
        }

        // ── Operations ─────────────────────────────────────────────────────────

        public void NewBlueprint(string displayName)
        {
            #if UNITY_EDITOR
            EnsureFolder();
            var asset = ScriptableObject.CreateInstance<BlueprintAsset>();
            asset.DisplayName = string.IsNullOrEmpty(displayName) ? "Untitled" : displayName;
            var path = AssetDatabase.GenerateUniqueAssetPath($"{BlueprintFolder}/{asset.DisplayName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Current = asset;
            RefreshSavedBlueprints();
            RefreshTestBuilding();
            #endif
        }

        public void Load(BlueprintAsset asset)
        {
            Current = asset;
            RefreshTestBuilding();
        }

        public void Save()
        {
            #if UNITY_EDITOR
            if (Current == null) return;
            EditorUtility.SetDirty(Current);
            AssetDatabase.SaveAssets();
            #endif
        }

        public void Rename(string newName)
        {
            #if UNITY_EDITOR
            if (Current == null || string.IsNullOrEmpty(newName)) return;
            Current.DisplayName = newName;
            var oldPath = AssetDatabase.GetAssetPath(Current);
            var newPath = AssetDatabase.GenerateUniqueAssetPath($"{BlueprintFolder}/{newName}.asset");
            AssetDatabase.RenameAsset(oldPath, System.IO.Path.GetFileNameWithoutExtension(newPath));
            EditorUtility.SetDirty(Current);
            AssetDatabase.SaveAssets();
            RefreshSavedBlueprints();
            #endif
        }

        public void DeleteCurrent()
        {
            #if UNITY_EDITOR
            if (Current == null) return;
            var path = AssetDatabase.GetAssetPath(Current);
            AssetDatabase.DeleteAsset(path);
            Current = null;
            RefreshSavedBlueprints();
            #endif
        }

        // ── Editing helpers ────────────────────────────────────────────────────

        // True if any of the room's occupied cells overlap an existing placement.
        public bool CellsOccupied(RoomDefinition def, Vector3Int gridPos, int rotation)
        {
            if (Current == null || def == null) return true;
            var size = (rotation & 1) == 0 ? def.GridSize
                                           : new Vector2Int(def.GridSize.y, def.GridSize.x);
            foreach (var existing in Current.Rooms)
            {
                var esize = (existing.Rotation & 1) == 0
                    ? existing.Definition.GridSize
                    : new Vector2Int(existing.Definition.GridSize.y, existing.Definition.GridSize.x);
                if (RectsOverlap(gridPos, size, existing.GridPosition, esize)) return true;
            }
            return false;
        }

        private static bool RectsOverlap(Vector3Int aPos, Vector2Int aSize,
                                          Vector3Int bPos, Vector2Int bSize)
        {
            if (aPos.y != bPos.y) return false;
            return !(aPos.x + aSize.x <= bPos.x ||
                     bPos.x + bSize.x <= aPos.x ||
                     aPos.z + aSize.y <= bPos.z ||
                     bPos.z + bSize.y <= aPos.z);
        }

        public void PlaceRoom(RoomDefinition def, Vector3Int gridPos, int rotation)
        {
            if (Current == null || def == null) return;
            if (CellsOccupied(def, gridPos, rotation)) return;
            Current.Rooms.Add(new RoomPlacement
            {
                Definition   = def,
                GridPosition = gridPos,
                Rotation     = rotation,
            });
            RefreshTestBuilding();
        }

        // Returns the index of the placement that owns `cell`, or -1 if none.
        public int FindPlacementAt(Vector3Int cell)
        {
            if (Current == null) return -1;
            for (int i = 0; i < Current.Rooms.Count; i++)
            {
                var p = Current.Rooms[i];
                var size = (p.Rotation & 1) == 0
                    ? p.Definition.GridSize
                    : new Vector2Int(p.Definition.GridSize.y, p.Definition.GridSize.x);
                if (cell.y == p.GridPosition.y &&
                    cell.x >= p.GridPosition.x && cell.x < p.GridPosition.x + size.x &&
                    cell.z >= p.GridPosition.z && cell.z < p.GridPosition.z + size.y)
                    return i;
            }
            return -1;
        }

        // ── RoomDefinition editing (Phase 2) ───────────────────────────────────

        // Writes the edited fields back to the .asset, marks dirty, and asks the
        // editor-side bridge to regenerate the .prefab so the changes show up live.
        // Caller is responsible for setting the new field values on `def` first.
        // Duplicate the asset of `original` with a single-character suffix (e.g.
        // "A" → Bathroom_2x2.A.asset). The new asset shares the prefab GUID with
        // the original, so geometry is identical — only furniture pools / rules
        // differ. The new variant is auto-added to every BuildingDefinition that
        // already references the original, so it shows up in the variant pool
        // immediately. Returns the new RoomDefinition or null on failure.
        public RoomDefinition SaveAsVariant(RoomDefinition original, string suffix)
        {
            #if UNITY_EDITOR
            if (original == null || string.IsNullOrWhiteSpace(suffix)) return null;
            suffix = suffix.Trim();
            if (suffix.Length != 1 || !char.IsLetterOrDigit(suffix[0]))
            {
                Debug.LogWarning("[BlueprintEditor] Variant suffix must be a single letter or digit (e.g. 'A', 'B', '2').");
                return null;
            }
            var family    = RoomVariants.GetFamilyName(original.name);
            var origPath  = AssetDatabase.GetAssetPath(original);
            var newName   = $"{family}.{suffix}";
            var newPath   = $"{System.IO.Path.GetDirectoryName(origPath)}/{newName}.asset";
            if (AssetDatabase.LoadAssetAtPath<RoomDefinition>(newPath) != null)
            {
                Debug.LogWarning($"[BlueprintEditor] Variant '{newName}' already exists.");
                return null;
            }
            if (!AssetDatabase.CopyAsset(origPath, newPath))
            {
                Debug.LogError($"[BlueprintEditor] Failed to copy '{origPath}' → '{newPath}'.");
                return null;
            }
            AssetDatabase.SaveAssets();
            var newDef = AssetDatabase.LoadAssetAtPath<RoomDefinition>(newPath);
            // Add to every building optional pool that already references the original.
            var buildingGuids = AssetDatabase.FindAssets("t:BuildingDefinition");
            foreach (var g in buildingGuids)
            {
                var bdPath = AssetDatabase.GUIDToAssetPath(g);
                var bd     = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(bdPath);
                if (bd == null) continue;
                bool referencesOriginal = false;
                foreach (var r in bd.OptionalPool)
                    if (r == original) { referencesOriginal = true; break; }
                if (!referencesOriginal) continue;
                var bso  = new SerializedObject(bd);
                var pool = bso.FindProperty("optionalPool");
                pool.arraySize = pool.arraySize + 1;
                pool.GetArrayElementAtIndex(pool.arraySize - 1).objectReferenceValue = newDef;
                bso.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            RefreshPalette();
            return newDef;
            #else
            return null;
            #endif
        }

        public void SaveAndRegenerateDefinition(RoomDefinition def)
        {
            if (def == null) return;
            #if UNITY_EDITOR
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            BlueprintRoomEditorBridge.RegeneratePrefab(def);
            #endif
            // The RoomDefinition's prefab just regenerated; existing instances in
            // the test building still hold the old geometry → re-spawn the building
            // so the user sees the updated shape immediately.
            RefreshTestBuilding();
        }

        // Removes the placement that contains the given grid cell, if any.
        public bool DeleteRoomAt(Vector3Int cell)
        {
            if (Current == null) return false;
            for (int i = 0; i < Current.Rooms.Count; i++)
            {
                var p = Current.Rooms[i];
                var size = (p.Rotation & 1) == 0
                    ? p.Definition.GridSize
                    : new Vector2Int(p.Definition.GridSize.y, p.Definition.GridSize.x);
                if (cell.y == p.GridPosition.y &&
                    cell.x >= p.GridPosition.x && cell.x < p.GridPosition.x + size.x &&
                    cell.z >= p.GridPosition.z && cell.z < p.GridPosition.z + size.y)
                {
                    Current.Rooms.RemoveAt(i);
                    RefreshTestBuilding();
                    return true;
                }
            }
            return false;
        }

        // ── Edge override helpers ──────────────────────────────────────────────

        // Normalises a cell pair so (A,B) and (B,A) compare equal in the override list.
        // Lower-(x,y,z) lexicographic ordering becomes A.
        private static void NormalizePair(ref Vector3Int a, ref Vector3Int b)
        {
            if (Compare(a, b) > 0) { var t = a; a = b; b = t; }
        }
        private static int Compare(Vector3Int a, Vector3Int b)
        {
            if (a.y != b.y) return a.y - b.y;
            if (a.x != b.x) return a.x - b.x;
            return a.z - b.z;
        }

        public EdgeState GetEdgeState(Vector3Int cellA, Vector3Int cellB)
        {
            if (Current == null) return EdgeState.Default;
            NormalizePair(ref cellA, ref cellB);
            foreach (var e in Current.EdgeOverrides)
                if (e.CellA == cellA && e.CellB == cellB) return e.State;
            return EdgeState.Default;
        }

        public void SetEdgeState(Vector3Int cellA, Vector3Int cellB, EdgeState state)
        {
            if (Current == null) return;
            NormalizePair(ref cellA, ref cellB);
            for (int i = 0; i < Current.EdgeOverrides.Count; i++)
            {
                if (Current.EdgeOverrides[i].CellA == cellA && Current.EdgeOverrides[i].CellB == cellB)
                {
                    if (state == EdgeState.Default) Current.EdgeOverrides.RemoveAt(i);
                    else Current.EdgeOverrides[i] = new EdgeOverride { CellA = cellA, CellB = cellB, State = state };
                    return;
                }
            }
            if (state != EdgeState.Default)
                Current.EdgeOverrides.Add(new EdgeOverride { CellA = cellA, CellB = cellB, State = state });
        }

        public void CycleEdgeState(Vector3Int cellA, Vector3Int cellB)
        {
            var current = GetEdgeState(cellA, cellB);
            var next = current switch
            {
                EdgeState.Default => EdgeState.Wall,
                EdgeState.Wall    => EdgeState.Open,
                EdgeState.Open    => EdgeState.Door,
                EdgeState.Door    => EdgeState.Default,
                _                  => EdgeState.Default,
            };
            SetEdgeState(cellA, cellB, next);
            RefreshTestBuilding();  // edge overrides don't affect raw prefabs yet
                                    // but keep the call for when Phase 6 wires them in
        }

        // ── Palette / saved-list refresh (editor-only) ────────────────────────

        private void RefreshPalette()
        {
            _palette.Clear();
            #if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets("t:RoomDefinition");
            foreach (var g in guids)
            {
                var path  = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath<RoomDefinition>(path);
                if (asset != null) _palette.Add(asset);
            }
            _palette.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            #endif
        }

        private void RefreshSavedBlueprints()
        {
            _savedBlueprints.Clear();
            #if UNITY_EDITOR
            // FindAssets warns + returns empty if the folder doesn't exist yet —
            // happens on first open before any blueprint has been saved. Create
            // the folder so the warning stops firing.
            EnsureFolder();
            var guids = AssetDatabase.FindAssets("t:BlueprintAsset", new[] { BlueprintFolder });
            foreach (var g in guids)
            {
                var path  = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath<BlueprintAsset>(path);
                if (asset != null) _savedBlueprints.Add(asset);
            }
            _savedBlueprints.Sort((a, b) => string.CompareOrdinal(a.DisplayName ?? "", b.DisplayName ?? ""));
            #endif
        }

        private void EnsureFolder()
        {
            #if UNITY_EDITOR
            if (!AssetDatabase.IsValidFolder(BlueprintFolder))
            {
                AssetDatabase.CreateFolder("Assets/Interiors", "Blueprints");
            }
            #endif
        }
    }
}
