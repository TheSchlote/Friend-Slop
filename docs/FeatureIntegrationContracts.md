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

CI enforces basic architecture drift through `ArchitectureGuardrailTests`: no new singleton-style globals, no direct scene searches in frame methods, no growing already-oversized runtime files, and no wrong-way runtime/UI/editor asmdef references.

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

## PR Checklist For Feature Agents

- No new `Instance` or `LocalPlayer` static global unless the owner explicitly approved an architecture change.
- No `FindObjectsByType`, `FindFirstObjectByType`, or `FindObjectOfType` inside `Update`, `FixedUpdate`, or `LateUpdate`.
- No runtime file grows past 400 lines; existing oversized files must not grow.
- No runtime assembly reference to UI or Editor.
- New gameplay logic has a test.
- Visual or layout changes are marked code complete and queued for human playtest.
