# Scene/Prefab Builder GUID-Determinism Audit

**Audit date:** 2026-04-27
**Files audited:** `Space Game/Assets/Scripts/Editor/FriendSlopSceneBuilder.cs` (~1297 lines), `Space Game/Assets/Scripts/Editor/FriendSlopMultiPlanetBuilder.cs` (~369 lines).

The motivating question: *can we vibe-code this project (no editor), keep regenerating prefabs/scenes from code, and not break asset references every time?*

Short answer: **mostly yes, with one specific failure mode to avoid.**

## How Unity asset identity works (quick refresher)

Unity assets have two layers of identity:

1. **GUID** — stored in the `.meta` file next to the asset. This is what cross-asset references resolve through. Stable across moves, renames, and rewrites of the asset, as long as the `.meta` file survives.
2. **FileID** (a.k.a. LocalFileID) — identifies *components and child GameObjects within* a prefab or scene. Stable as long as you keep the same in-memory object identity when saving back. Regenerates if you reconstruct the object hierarchy from scratch and `SaveAsPrefabAsset` over the existing path.

**Cross-asset references = (GUID, FileID).** GUID gets you to the file; FileID gets you to the object inside it. Either churning is a broken reference.

## What's stable in the current builders

### Materials — stable.
`CreateMaterial` (line 1296) does the right thing: `LoadAssetAtPath<Material>(path)` first, only `CreateAsset` if missing. It mutates color/keyword properties on the existing material instance in place. The material's GUID is preserved across builds.

### Top-level prefab GUIDs — stable.
`PrefabUtility.SaveAsPrefabAsset(root, path)` to an existing path preserves the existing `.prefab.meta` GUID. So:

- `Assets/Prefabs/FriendSlopPlayer.prefab` — GUID stable.
- `Assets/Prefabs/RoundManager.prefab` — GUID stable.
- `Assets/Prefabs/RoamingMonster.prefab` — GUID stable.
- `Assets/Prefabs/Loot/<Name>.prefab` — GUID stable for any spec whose name doesn't change.
- `Assets/DefaultNetworkPrefabs.asset` — created once via `LoadAssetAtPath` + `CreateAsset` fallback (line 452), then mutated. GUID stable.

This means the `NetworkPrefabsList` keeps resolving correctly across rebuilds, and any external asset (e.g. a `PlanetDefinition.asset` referencing a prefab) does too.

### Scene GUID — stable.
`EditorSceneManager.SaveScene(scene, ScenePath)` over the existing path preserves the `.unity.meta` GUID.

### Repair path — well-behaved.
`RepairPrototypeScene` and `TryRepairOpenPrototypeScene` (lines 121, 156) load existing prefabs first and only build missing ones. They preserve in-prefab object identity for everything that already exists.

## What's NOT stable

### Sub-object FileIDs inside prefabs — churn on full Rebuild.
The `Tools/Friend Slop/Rebuild Prototype Scene` path (`BuildPrototypeScene`, line 47) calls each `BuildXPrefab` method unconditionally:

```csharp
var playerPrefab = BuildPlayerPrefab(materials);
var roundManagerPrefab = BuildRoundManagerPrefab();
var lootPrefabs = BuildLootPrefabs(materials);
var monsterPrefab = BuildMonsterPrefab(materials);
```

Each `BuildXPrefab` does:

```csharp
var root = new GameObject(...);
// ... add components, child GameObjects ...
var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);  // overwrites
Object.DestroyImmediate(root);
```

The `SaveAsPrefabAsset` overwrite preserves the **prefab file's GUID** (Unity reuses the existing `.meta`), but the in-prefab object hierarchy is brand-new in-memory, so **every child GameObject and component gets a fresh FileID** in the saved YAML.

**Concrete consequence.** Any external `.asset` or scene that holds a reference to a *sub-object* of one of these prefabs (e.g. a serialized field referencing the `Remote Body Renderer` inside the player prefab) silently breaks after a Rebuild — the GUID still resolves to the file, but the FileID points at "no such object."

We don't currently have any such references in committed assets (top-level prefab references only), so the practical impact today is *noisy git diffs*, not broken behavior. But the failure mode is sitting there waiting.

### Scene FileIDs — churn on full Rebuild.
`BuildPrototypeScene` calls `EditorSceneManager.NewScene(EmptyScene, Single)` then saves over `Assets/Scenes/FriendSlopPrototype.unity`. Same story: scene GUID preserved, every in-scene GameObject FileID regenerates.

Because all in-scene references are scene-local (a scene's GameObjects reference each other, not external sub-objects), this doesn't break references — but it does churn the YAML and bloat history.

### Loot prefab loss when renamed.
`BuildLootPrefabs` derives the prefab path from `spec.Name` via `SanitizeAssetName` (line 388). If a loot spec is renamed, a Rebuild creates a *new* `.prefab` under the new name and orphans the old one (with its old GUID). `NetworkPrefabsList` will then contain a dead reference until manually cleaned up. **Avoid renaming loot in `GetLootSpecs` without also deleting the old `.prefab`.**

## Risk verdict

For our current commit (top-level references only):

| Action | Behavior |
|---|---|
| Run `Repair Prototype Scene` | Idempotent. Adds nothing if everything exists. Safe. |
| Run `Rebuild Prototype Scene` | Top-level GUIDs survive; sub-object FileIDs regenerate. Massive YAML diff in `.prefab` and `.unity` files. References that are sub-object-deep (none today) would silently break. |
| Add a new loot spec | `Repair` adds the new prefab, leaves others alone. Safe. |
| Rename a loot spec | Orphans the old prefab. Manual cleanup required. |
| Add a new ScriptableObject `.asset` | Independent of these builders. Safe. |

## Recommendations

These are deferred to a later step (the "carve `FriendSlopSceneBuilder` into per-system builders" milestone). Capturing them here so they aren't lost.

1. **Make every `BuildXPrefab` load-or-create.** Match the pattern used by `CreateMaterial`: try `LoadAssetAtPath` first; if found, mutate fields on the existing prefab instance via `PrefabUtility.LoadPrefabContents` + `SavePrefabAsset`. Only construct from scratch when the file is missing.
2. **Make `BuildPrototypeScene` open the existing scene** instead of `NewScene(EmptyScene)`. Then the in-scene FileIDs are preserved across rebuilds.
3. **Detect orphaned loot.** When generating loot prefabs, scan `Assets/Prefabs/Loot` for files that don't correspond to any current spec name and warn (or auto-delete with confirmation).
4. **Block `Rebuild` from running in CI/headless without an explicit flag.** The auto-build marker file is the right primitive; tighten it so a `Repair` is the default headless behavior.
5. **Long term, split the monolith.** `PlayerPrefabBuilder.cs`, `LaunchpadPrefabBuilder.cs`, `PlanetSceneBuilder.cs`, `UICanvasBuilder.cs`. Each file has a clear single responsibility and its own load-or-create flow.

## How to verify locally

After any change to a builder, the right validation sequence is:

```powershell
# 1. Capture pre-change asset state.
git status

# 2. Run Repair (not Rebuild) in batch mode.
& 'T:\Unity\6000.3.4f1\Editor\Unity.exe' `
   -batchmode -projectPath 'T:\Repos\Friend-Slop\Space Game' `
   -executeMethod FriendSlop.Editor.FriendSlopSceneBuilder.RepairPrototypeSceneBatch `
   -logFile "$env:TEMP\friend-slop-repair.log" -quit

# 3. The git diff should be empty (or only the actually-changed asset).
git status
git diff --stat
```

If `Repair` produces a non-empty diff on a clean tree, the builder has accidentally become non-idempotent and needs to be fixed before merging.
