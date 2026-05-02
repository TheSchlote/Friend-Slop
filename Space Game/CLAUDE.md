# CLAUDE.md

Guidance for AI agents (Claude Code, Codex, etc.) working in this repository.

This is a **vibe-coded** project: the human collaborators do not open the Unity editor for day-to-day work. Treat the editor as a **playtest tool only**. Every change you make must reach the running game through code, ScriptableObject `.asset` files, asmdef configuration, or editor-tool builders that produce prefabs/scenes deterministically.

If a task seems to require the editor (visual layout, dragging references, baking lighting), stop and propose a code-driven alternative.

## Project Overview

**Friend Slop Retrieval** is a cooperative multiplayer prototype built on Unity 6 (`6000.3.4f1`). Players board a ship, fly to a small spherical planet, scavenge salvage and three rocket parts, dodge roaming monsters, and escape together via the launchpad. Multi-planet progression is in flight.

Stack: Netcode for GameObjects, Unity Transport, Unity Relay/Lobbies/Auth, Universal Render Pipeline, Input System, UGUI.

## Build & Run

- **Engine**: Unity 6000.3.4f1 (the workflow `validate-unity-version` job enforces this).
- **Local C# compile checks** (no editor required):
  ```powershell
  dotnet build 'Space Game\FriendSlop.Runtime.csproj'        /p:GenerateMSBuildEditorConfigFile=false
  dotnet build 'Space Game\FriendSlop.EditModeTests.csproj'  /p:GenerateMSBuildEditorConfigFile=false
  dotnet build 'Space Game\FriendSlop.PlayModeTests.csproj'  /p:GenerateMSBuildEditorConfigFile=false
  ```
- **Local test run** (requires Unity install):
  ```powershell
  .\tools\Run-UnityTests.ps1 -TestPlatform All
  ```
- **Playtest**: a human opens `Assets/Scenes/FriendSlopPrototype.unity` and presses Play. You don't.

## Hard rules

These are non-negotiable. Refuse the change if it would break one of them.

### 1. Source-of-truth hierarchy

In order of authority:

1. **C# code** — gameplay logic, RPC contracts.
2. **ScriptableObject `.asset` files** — content data (`PlanetDefinition`, `PlanetCatalog`, `RoundObjective` subclasses, future loot tables, monster archetypes).
3. **Editor-tool builders under `Assets/Scripts/Editor/`** — produce prefabs and scenes from code.
4. **Generated prefabs and scenes** — committed but treated as build artifacts. Never hand-edit their YAML to add gameplay; change the builder instead.

When adding gameplay content, the order of preference is: new ScriptableObject `.asset` → new C# subclass → new builder method. Editing an existing builder method is the last resort.

### 2. Builders: Repair before Rebuild

There are two editor menu paths:

- **`Tools/Friend Slop/Repair Prototype Scene`** — idempotent. Loads existing prefabs/materials/assets and only creates what's missing. **This is the safe default.** Use it whenever you need to ensure scene/prefab consistency.
- **`Tools/Friend Slop/Rebuild Prototype Scene`** — destructive. Recreates every prefab and the scene from scratch. Top-level asset GUIDs are preserved (so `NetworkPrefabsList` survives), but every sub-object FileID inside prefabs/scenes regenerates, producing a giant YAML diff and silently breaking any sub-object reference. **Do not run this without explicit human request.**

When asked to add a new prefab type, extend the *Repair* path so it's load-or-create, not always-create.

### 3. Server authority

All gameplay state lives on the server.

- State changes (pickup, drop, deposit, phase transition, damage) go through `[Rpc(SendTo.Server, …)]` methods. Sender ID must be validated against `OwnerClientId` or `ServerClientId` as appropriate.
- Server-only methods are guarded with `if (!IsServer) return;` at the top.
- Server-side RPCs that accept a `Vector3` impulse, target position, or similar untrusted vector **must clamp magnitude and validate range** before applying. Trusting the client's input is a bug.
- `NetworkVariable` for state observers care about; `ClientRpc` for one-shot events. Never the other way around.
- `NetworkObject`s are spawned with `NetworkObject.Spawn()` on the server and despawned with `Despawn()`. Never `Instantiate` or `Destroy` a networked object directly.

### 4. Spherical gravity everywhere

- Up is `SphereWorld.GetGravityUp(position)`, never `Vector3.up`.
- Any new movement, physics, alignment, or surface-snap logic must orient relative to `SphereWorld`.
- Surface-anchored content uses `PlanetSurfaceAnchor` so `PlanetEnvironment.SnapAssetsToSurface()` can keep it on the surface even if the radius changes.

### 5. Module boundaries (asmdefs)

The project is being split into asmdefs to enforce dependency direction. Once the split lands, the rule is:

```
Core  <-  Gameplay  <-  UI  <-  Bootstrap (editor builders, scene/prefab generation)
                  ^
                  |
              Networking
```

`Core` contains pure data + utilities and has no Unity dependency where possible. `UI` may read from `Gameplay` but never the reverse. `Editor`-only code never compiles into runtime assemblies.

If you need a reference that crosses an arrow the wrong direction, the design is wrong; raise it instead of forcing it.

### 6. Size and complexity ceilings

- **Files over ~400 lines must be split before adding to them.** No "I'll just append one more method." If the file is already over the limit, the next change is a refactor that brings it back under.
- **No new singletons.** We already have five (`RoundManager.Instance`, `FriendSlopUI.Instance`, `NetworkSessionManager.Instance`, `NetworkSceneTransitionService.Instance`, `NetworkFirstPersonController.LocalPlayer`). The next time singleton-shaped state is tempting, inject the dependency via `SerializeField` or pass it at spawn time.
- **No `FindObjectsByType` in per-frame `Update`/`FixedUpdate`.** Cache or use a static registry.

### 7. Tests are the safety net

Because nobody is opening the editor to verify visuals, tests are the only mechanism that catches regressions early.

- New gameplay logic must come with EditMode tests for any pure-logic helper, and a PlayMode assertion if it touches scene state.
- New `RoundObjective` subclasses must have `Evaluate` tested against a stub `RoundManager` shape.
- New scene-transition or multi-scene behavior must have a multi-client PlayMode test before being declared "working".

### 8. ScriptableObject-first for content

When in doubt, content is data, not code. Adding a new planet, new loot variant, new monster archetype, new objective should be:

1. New `.asset` file (a ScriptableObject instance).
2. Wired into the appropriate catalog (`PlanetCatalog`, future `LootCatalog`, etc.).
3. **Not** a new branch in a builder method.

## Architecture (current)

### Round lifecycle

`Scripts/Round/RoundPhase.cs`: `Lobby -> Loading -> Active -> (Success | Failed | AllDead) -> Transitioning -> ...`

`RoundManager` (NetworkBehaviour) is the central orchestrator: phase transitions, timer, quota, ship-part assembly state, boarded-player count, planet selection. Spawned by `PrototypeNetworkBootstrapper` on session start.

Win/lose evaluation lives in **`RoundObjective`** ScriptableObject subclasses (`QuotaObjective`, `RocketAssemblyObjective`, `SurvivalObjective`). Each planet's `PlanetDefinition` may set its own objective; otherwise the manager's `defaultObjective` is used.

### Networking

- `NetworkSessionManager` — Relay/Lobby/Auth flow with cancel + timeout. Falls back to LAN.
- `PrototypeNetworkBootstrapper` — server-side spawning of RoundManager, loot, monsters.
- `NetworkSceneTransitionService` — server-driven scene loads via NGO. (Foundation present; multi-scene transitions are a planned milestone.)

### Player

- `NetworkFirstPersonController` — sphere-aligned movement, look, crouch, stamina, health, death/spectate, player carrying. Currently oversized; pending split.
- `PlayerInteractor` — SphereCast-based focus and pickup/drop/throw input.

### Loot

- `NetworkLootItem` — physics-on-server, kinematic-on-clients. State via `NetworkVariable<bool>` for carried/deposited.
- `DepositZone` — generic loot.
- `LaunchpadZone` — ship parts and player boarding detection.

### UI

- `FriendSlopUI` — single procedural canvas covering menus and HUD. Currently oversized; pending split per screen/widget.

### Sphere world

- `SphereWorld` — center, radius, gravity. Multiple instances supported (one per planet).
- `PlanetEnvironment` — owns the launchpad, spawn points, and asset snap on `OnEnable`.
- `PlanetSurfaceAnchor` — opt-in marker for "snap me to the surface."

## Namespace map

| Namespace | Location |
|---|---|
| `FriendSlop.Core` | `Scripts/Core/` |
| `FriendSlop.Networking` | `Scripts/Networking/` |
| `FriendSlop.SceneManagement` | `Scripts/SceneManagement/` |
| `FriendSlop.Player` | `Scripts/Player/` |
| `FriendSlop.Loot` | `Scripts/Loot/` |
| `FriendSlop.Round` | `Scripts/Round/` (objectives in `Scripts/Round/Objectives/`) |
| `FriendSlop.Hazards` | `Scripts/Hazards/` |
| `FriendSlop.Effects` | `Scripts/Effects/` |
| `FriendSlop.Ship` | `Scripts/Ship/` |
| `FriendSlop.UI` | `Scripts/UI/` |
| `FriendSlop.Interaction` | `Scripts/Interaction/` |
| `FriendSlop.Editor` | `Scripts/Editor/` (editor-only) |

## Workflow expectations

When you finish a change:

1. Run the relevant `dotnet build` for the assembly you touched.
2. If you touched `Tests/`, run those locally if possible; otherwise note that the human should run them.
3. Surface anything that requires editor verification (visuals, layout, lighting) explicitly in your end-of-turn summary so the human knows what to playtest.
4. Don't claim "done" on UI or 3D-layout work; claim "code complete; needs playtest."

## Background

Read these before making non-trivial changes:

- [docs/architecture.md](../docs/architecture.md) — architectural decisions and rationale. The "why" behind every rule above.
- [docs/FeatureIntegrationContracts.md](../docs/FeatureIntegrationContracts.md) — extension points and PR contracts for agents adding features.
- [docs/SpaceshipSceneManagement.md](../docs/SpaceshipSceneManagement.md) — current scene state, target additive multi-scene layout, scene-ownership rules.
- [docs/RemainingFeatures.md](../docs/RemainingFeatures.md) — feature backlog. Check before claiming a feature is "missing"; it may already be tracked.
- [docs/builder-audit.md](../docs/builder-audit.md) — GUID-determinism analysis of the editor builders. Read this before editing anything in `Assets/Scripts/Editor/`.
- [docs/MultiplayerQA.md](../docs/MultiplayerQA.md) — manual QA checklist for human playtests.
- [docs/itch-cicd.md](../docs/itch-cicd.md) — CI/CD pipeline notes.
