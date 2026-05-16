# Feature Integration Contracts

This is the handoff for humans and AI agents adding new features to Friend Slop Retrieval. The goal is not to slow feature work down; it is to make features land through stable extension points instead of growing one-off branches in managers, UI, or builders.

Before starting a feature, identify which contract below applies. If none fits, add or propose a new contract before implementing the feature.

## Required Shape

Every feature PR should answer these questions in its summary:

- What feature contract did it use?
- What runtime files did it touch?
- What ScriptableObject assets or prefabs did it add?
- What server authority path validates the player action?
- What EditMode or PlayMode test proves the integration?
- Does any visual/layout work still need human playtest?

CI enforces basic architecture drift through `ArchitectureGuardrailTests`: no new singleton-style globals, no direct scene searches in frame methods, no growing already-oversized runtime files, and no wrong-way runtime/UI/editor asmdef references. The only approved project-owned globals are documented in [SingletonAudit.md](SingletonAudit.md).

## New Planet

Use this for any playable planet, biome, or mission location.

Expected path:

- Add or update a `PlanetDefinition` asset.
- Register it in `PlanetCatalog`.
- If it has unique geometry, give it a `GameSceneDefinition` and a dedicated `Planet_*` scene.
- The scene must contain a `PlanetEnvironment`, launchpad, player spawns, `SphereWorld`, and ship teleporter.
- Let `PlanetSceneValidator` catch missing Build Settings entries and missing scene wiring.

Do not:

- Add planet-specific branches to `RoundManager`.
- Hand-edit scene YAML for gameplay wiring.
- Remove same-tier scene fallback until every planet in that tier owns a dedicated scene.

Tests:

- EditMode validation for catalog/scene wiring.
- PlayMode smoke coverage when adding or changing scene-transition behavior.

## New Objective

Use this for quota variants, survival modes, collection goals, boss goals, or future win conditions.

Expected path:

- Create a `RoundObjective` subclass.
- Implement `ServerInitialize`, `Evaluate`, `BuildHudStatus`, `BuildSuccessText`, and `BuildFailureText`.
- Create a ScriptableObject asset for the objective.
- Assign the objective through `PlanetDefinition.Objective` or the `RoundManager` default.

Do not:

- Add objective-specific checks directly to `RoundManager.Update`.
- Hardcode objective result copy in `FriendSlopUI`.

Tests:

- EditMode tests for `Evaluate`.
- EditMode tests for HUD/result copy when it is player-facing.

## New Loot Or Item

Use this for salvage, ship parts, inventory items, and future economy content.

Expected path:

- Add or update a prefab under the loot prefab conventions.
- Keep networked item state on `NetworkLootItem`.
- Spawn only on the server and use `NetworkObject.Spawn`.
- Prefer data assets/catalogs for value, rarity, budget, and spawn rules.

Do not:

- Instantiate or destroy networked loot directly on clients.
- Encode loot tables as branches in scene builders.
- Trust client-provided throw/drop vectors without server-side clamping.

Tests:

- EditMode tests for pure selection/budget logic.
- PlayMode or integration coverage for new networked spawn/deposit behavior.

## New UI Screen Or Panel

Use this for settings, station panels, mission briefings, score summaries, and future menus.

Expected path:

- Create a standalone UI component for the new screen.
- Let `FriendSlopUI` open/close or route to it only through a small public method or serialized reference.
- Keep screen-specific state and layout out of the main `FriendSlopUI` partials.
- Use `PlayerPrefs` only for local user preferences, not gameplay state.

Do not:

- Add another large procedural section to `FriendSlopUI`.
- Make gameplay systems depend on UI types.
- Add a UI singleton.

Tests:

- EditMode tests for formatting, settings persistence helpers, or pure UI state helpers.
- Human playtest for visual layout.

## New Networked Interaction

Use this for player actions that affect world state: stations, revives, damage, pickups, deposits, travel, purchases, or utility use.

Expected path:

- Client sends a server RPC request.
- Server validates sender identity from `RpcParams`.
- Server clamps untrusted vectors/amounts/indices before applying them.
- Server updates `NetworkVariable`s or sends a one-shot client RPC.

Do not:

- Let clients directly mutate gameplay state.
- Infer authority from local ownership without checking the sender.
- Use static global state when an event, serialized reference, or existing manager can own the coordination.

Tests:

- EditMode tests for validation helpers.
- PlayMode tests for multi-client authority-sensitive behavior when feasible.

## New Networked Spawn

Use this for any code that creates a `NetworkObject` at runtime: loot, monsters, doors, hazards, blueprints, interior content, future projectiles. Read [NetworkObjectSceneOwnership.md](NetworkObjectSceneOwnership.md) first; this contract is the short version.

Expected path:

- Spawn on the server only (`if (!IsServer) return;`).
- If the target scene is not the caller's current active scene, swap the active scene around the whole batch before `Instantiate` so `NetworkObject.Spawn(destroyWithScene: true)` captures the right `SceneOriginHandle`. Reference implementations: `ActiveSceneScope` in `PrototypeNetworkBootstrapper.Spawning.cs`; the inlined try/finally in `PlanetLootSpawner.TrySpawnNow`.
- Pass `destroyWithScene: true` unless the object genuinely outlives its scene (`false` puts it in `DontDestroyOnLoad`, which is almost never the right answer for planet/interior content).
- Despawn on the server with `NetworkObject.Despawn(destroy: true)` when the owning scene unloads or the round transitions.

Do not:

- Call `SceneManager.MoveGameObjectToScene` after `NetworkObject.Spawn`. NGO scene management fights post-Spawn moves with `EnableSceneManagement = true`.
- `Instantiate`/`Destroy` a networked prefab directly.
- Trust client-provided spawn positions or counts.
- Call `Object.FindObjectsByType<T>` over an NGO type without filtering by `NetworkObject.IsSpawned` when you care about live runtime state. NGO parks `NetworkPrefabsList` templates as inactive instances in the Bootstrap scene.

Tests:

- PlayMode assertion that the spawned objects land in the intended scene (`gameObject.scene == expected`).
- Use `CountSpawned<T>` (see `FriendSlopPrototypeSmokeTests.cs`) for spawn-count assertions; raw `FindObjectsByType` counts will include prefab templates.

## New Ship Station

Use this for pilot controls, holographic board flows, customization bench, storage, or utilities.

Expected path:

- Keep `ShipStation` as the occupancy/claim layer.
- Add a separate station-flow component for station-specific behavior.
- Use an interface or event to decouple the station from UI screens.
- Put station UI in its own panel/component.

Do not:

- Put all station behavior into `ShipStation`.
- Add station-specific code to `RoundManager`.

Tests:

- EditMode tests for claim/release or station-flow state.
- PlayMode tests for networked station behavior.

## New Building Type

Use this for a new procedural interior (warehouse, lab, hangar, etc.) that should be rolled, not hand-authored. Read [InteriorSystem.md](InteriorSystem.md) for the full pipeline.

Expected path:

- Add a new `BuildingDefinition` asset under `Assets/Interiors/Buildings/`. Configure room counts, floor counts, required rooms, optional room pool, special-room targets.
- Register it in the `InteriorCatalog` your `InteriorEntrance` points at.
- If new room shapes are needed, add `RoomDefinition` assets (see "New Furniture Definition" below for the related furniture path).
- Make sure all generation inputs are functions of the seed. No `Time.time`, no `Random.Range` without going through the seeded RNG, no order dependence on `FindObjectsByType`. The determinism contract is in D-009.

Do not:

- Branch in `InteriorLayoutGenerator` on building name.
- Hand-edit interior scene YAML.
- Introduce per-furniture `NetworkObject`s — furniture is deterministic-but-local.

Tests:

- EditMode test that `BuildingDefinition.RoomPool` returns the expected set (combines `optionalPool` + `requiredRooms.Definition`). This catches future `FormerlySerializedAs` renames before they ship.
- EditMode generator test if the new building introduces a new constraint shape.

## New Furniture Definition

Use this for adding furniture variety to existing room types.

Expected path:

- Add a new `FurnitureDefinition` asset. Configure anchor placement (`Wall` / `Corner` / `Center` / `Tabletop` / `AroundTable` / `WallHanging`), tags, footprint, prefab, weight, interactable flag.
- Add it to the relevant `RoomDefinition.furnitureRules` or tag bucket so the picker (`PickFurnitureForAnchor`) sees it.
- Make sure footprint and anchor placement match the prefab; the placer relies on the metadata, not the visual.

Do not:

- Spawn the furniture as a `NetworkObject` for cosmetic-only pieces.
- Bypass `FurnitureAnchor` and place the piece by world position; anchor selection is part of the determinism contract.
- Modify `InteriorSceneBootstrapper.Furniture.cs` to add a per-piece special case.

Tests:

- EditMode test against the picker helpers (`HasTagOverlap`, `IsCappedOut`, `PickFurnitureForAnchor`, `OverlapsExisting` in `InteriorSceneBootstrapper.Furniture.cs`) when the new piece exercises a tag or cap edge case.

## New Blueprint

Use this for a hand-authored interior (homebase, signature building) where the design wants explicit room placement instead of procedural generation.

Expected path:

- Open the blueprint editor in a playtest (`F1` toggle in the blueprint test scene). Author rooms, edge overrides (`Wall` / `Open` / `Door`), and per-slot variant choices.
- Save the result as a `BlueprintAsset` under `Assets/Interiors/Blueprints/`.
- Wire a `BlueprintEntrance` (instead of `InteriorEntrance`) at the building's exterior, pointing at the blueprint.
- The runtime path is `BlueprintLayoutBuilder.Build(blueprint, definition)` → same `InteriorLayout` shape as procedural → same bootstrapper.

Do not:

- Add procedural fallback logic to the blueprint path. `ApplyDoorPolicy` is intentionally bypassed because edge state is user-authored.
- Mix blueprint + procedural in the same building. Pick one entry point.

Tests:

- EditMode `BlueprintLayoutBuilderTests`: empty/null blueprint returns empty layout, room placement, socket adjacency, edge-state overrides, variant picking, per-slot overrides.

## PR Checklist For Feature Agents

- No new `Instance` or `LocalPlayer` static global unless the owner explicitly approved an architecture change.
- No `FindObjectsByType`, `FindFirstObjectByType`, or `FindObjectOfType` inside `Update`, `FixedUpdate`, or `LateUpdate`.
- Any `FindObjectsByType<T>` over an NGO type filters by `NetworkObject.IsSpawned` (or uses `CountSpawned<T>`) when it cares about live game state.
- Any code that spawns a `NetworkObject` into a non-caller scene uses an active-scene swap around `Instantiate` + `Spawn`; no `MoveGameObjectToScene` after `Spawn`. `destroyWithScene: true` unless the object genuinely outlives its scene.
- No runtime file grows past 400 lines; existing oversized files must not grow.
- No runtime assembly reference to UI or Editor.
- New gameplay logic has a test.
- Visual or layout changes are marked code complete and queued for human playtest.
