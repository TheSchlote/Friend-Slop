# Friend Slop Retrieval — Architecture

This document captures the *why* behind the structural choices in this repo. It is the canonical reference for AI agents and human collaborators when a question of "should we do X or Y" comes up. If a decision recorded here turns out to be wrong, update this file in the same change that overturns it.

## Working context

- Two-person hobby project.
- Lead architect is a senior C# developer; co-author is learning by doing.
- Day-to-day development is **vibe-coded**: changes are made by AI agents (Claude Code, Codex) editing source files, ScriptableObjects, and editor-tool builders. The Unity editor is opened only for playtesting and the occasional human visual check.
- Goal: a polished, fun multiplayer prototype that supports rapid feature iteration without the codebase collapsing under its own weight.

These constraints inform every decision below.

## Decision log

### D-001: Source-of-truth is code (and ScriptableObjects), not the editor

**Decision.** Scenes and prefabs are committed but treated as build artifacts of the editor-tool builders under `Assets/Scripts/Editor/`. New gameplay content lands as either C# code or a ScriptableObject `.asset` file, never by hand-editing a prefab/scene YAML.

**Why.** AI agents can't operate the editor. If the scene file is the source of truth, every gameplay change requires a human in the editor — which is the bottleneck this project specifically wants to avoid. By making generation the source of truth, every change is a code change, fully reviewable in a diff, fully reproducible.

**Cost.** No drag-and-drop wiring. Field references must be configured in code via `SerializedObject` or by relying on `RequireComponent` + `GetComponent`. Visual layout is done by math (this is fine for a sphere world: positions are trig).

### D-002: Repair, not Rebuild

**Decision.** The day-to-day editor command is `Tools/Friend Slop/Repair Prototype Scene`, which is idempotent (load-or-create). The destructive `Tools/Friend Slop/Rebuild Prototype Scene` is reserved for explicit human request.

**Why.** `PrefabUtility.SaveAsPrefabAsset` preserves a prefab's top-level GUID when overwriting an existing path, but child object FileIDs (the IDs that scene/prefab references resolve through) regenerate every time. Full rebuilds therefore produce massive YAML diffs and silently break any sub-object reference. The Repair path avoids this by loading existing assets and only creating what's missing.

**Cost.** Builders take a little more code (the load-or-create branch). The Rebuild button still exists as an escape hatch.

### D-003: Server-authoritative state for everything

**Decision.** All gameplay state lives on the server. Clients send `ServerRpc` requests; the server validates and updates `NetworkVariable`s; clients react via `OnValueChanged` or one-shot `ClientRpc`s. Every `ServerRpc` validates `OwnerClientId` / `ServerClientId` and clamps untrusted vector inputs.

**Why.** Even in a friend group, "trust the client" produces bugs that look like exploits. Modded clients, dropped packets, and just-plain-broken-code all manifest as client-side state divergence. Server authority makes those failures local and visible.

**Cost.** A round-trip latency on every interaction. Acceptable for the gameplay style (slower-paced co-op salvage).

### D-004: ScriptableObject-first for content

**Decision.** Adding a new planet, monster archetype, loot variant, or objective is a new `.asset` file, not a new code branch. Catalogs (`PlanetCatalog`, future `LootCatalog`, future `MonsterCatalog`) hold references; runtime code reads from the catalog.

**Why.** Vibe-coded projects bloat fast when each new feature lives as a code branch. Data-driven content scales linearly: N planets is N tiny files, not N big methods. It also gives the human the option to edit values in the inspector during a playtest without recompiling.

**Cost.** A bit of plumbing per content type (the catalog and the loader). One-time investment.

### D-005: Multi-scene network architecture (additive)

**Decision (in flight).** Reaffirms the scene plan already documented in [SpaceshipSceneManagement.md](SpaceshipSceneManagement.md). The runtime is split into four scene roles, all loaded **additively**:

| Role | Loaded when | Contents |
|---|---|---|
| `Bootstrap` | Always (build index 0) | `NetworkManager`, `NetworkSessionManager`, `FriendSlopUI`, `NetworkSceneTransitionService`, persistent audio, save data. No gameplay content. |
| `ShipInterior` | Whole network session | Ship geometry, ship spawn points, stations, customization hooks, between-planet gameplay. |
| `Planet_<name>` | While visiting that planet | `SphereWorld`, `PlanetEnvironment`, monsters, loot spawn points, planet-specific managers. One planet loaded at a time. |
| `Travel_<name>` | Optional, during inter-planet travel | Route hazards, pilot minigames. Owns no player objects. |

The server triggers loads via `NetworkManager.SceneManager.LoadScene(path, LoadSceneMode.Additive)`. NGO synchronizes the load to all clients and re-synchronizes any in-scene `NetworkObject`s. Planets are unloaded when leaving them; Bootstrap and ShipInterior persist for the whole session.

**Why additive instead of single-mode swap.** This is the AAA-co-op pattern (Lethal Company, Risk of Rain 2). Going additive lets us:
- Keep the ship visible (window into space) while a planet is "loaded but distant."
- Avoid re-loading the ship every time players come home — a major UX win.
- Hold persistent systems (audio, AI, save) in Bootstrap with no risk of re-init.
- Keep planet/ship spawn sets cleanly separated (rule from `SpaceshipSceneManagement.md`: "never infer location from current scene name").

**Cost.** Higher discipline required around scene ownership. Gameplay code must never `FindObjectOfType` across the additive set without filtering by scene. The `RoundManager` needs to know which planet scene is active, not "the scene."

**Status.** `NetworkSceneTransitionService` skeleton exists at `Assets/Scripts/SceneManagement/`. Not yet consumed by gameplay flow. Gating criteria for marking this complete:

1. `Bootstrap`, `ShipInterior`, and at least one `Planet_*` scene exist as separate assets and are in Build Settings.
2. `GameSceneCatalog` is the only place runtime code looks up scenes (no string literals).
3. A multi-client PlayMode test exercises ship → planet → ship transitions and asserts player/round state at each step.
4. Late-join is tested for each phase (lobby, ship-only, ship+planet active).

### D-006: Asmdef boundaries (planned)

**Decision.** Split runtime code into asmdefs that enforce the dependency graph:

```
FriendSlop.Core         <-  FriendSlop.Networking  <-  FriendSlop.Gameplay  <-  FriendSlop.UI
                                                                                        ^
                                                                                        |
                                                                              FriendSlop.Bootstrap
                                                                                  (Editor only)
```

`Core` holds pure data and utilities (`SphereWorld`, `RoundStateUtility`, `JoinCodeUtility`, `CarrySyncUtility`, `RoundPhase`, `ShipPartType`). It should have minimal Unity dependency where feasible.

`Gameplay` holds NetworkBehaviours. `UI` may read `Gameplay` state but never the reverse. `Bootstrap` is editor-only and depends on everything (it generates the scene).

**Why.** asmdefs are the only mechanism in Unity that physically prevent a wrong-direction dependency. Once enforced, AI agents can't accidentally couple UI logic into gameplay or vice versa.

**Cost.** A handful of `.asmdef` files and some namespace cleanup. One-time pain, permanent payoff.

**Status.** Not yet started. Planned as a single focused PR.

### D-007: File-size and singleton limits

**Decision.** Files stop at ~400 lines. New singletons require explicit justification.

**Why.** Both are leading indicators that the code is growing in the wrong shape. AI agents naturally append to existing files and reach for global state. A hard cap forces the right reaction (split, inject) at the moment when the cost is still small.

**Cost.** Some refactor churn. Worth it.

**Status.** Two files currently exceed the cap (`FriendSlopUI.cs` ~1455, `NetworkFirstPersonController.cs` ~1321) and `FriendSlopSceneBuilder.cs` ~1297. These are flagged for split before any further additions.

### D-008: Tests at architectural seams

**Decision.** EditMode tests cover pure-logic helpers (already done for `JoinCodeUtility`, `RoundStateUtility`, `CarrySyncUtility`). Going forward, every new `RoundObjective` subclass requires `Evaluate` tests; phase transitions in `RoundManager` get a state-machine test; multi-scene transitions get a multi-client PlayMode test. The PlayMode smoke test stays as a top-level "the prototype boots" check.

**Why.** With no editor-driven verification, tests are the only thing standing between a vibe-coded change and a silently broken playtest. The seams (objective evaluation, phase transitions, scene transitions, RPC validation) are where logic goes wrong invisibly.

**Cost.** Slower per-feature delivery. Bug-discovery time drops dramatically.

## Anti-patterns to refuse

The following are explicitly out of scope for this project. If you find yourself reaching for one, stop and propose an alternative.

- **Hand-editing prefab/scene YAML for gameplay configuration.** Use a builder.
- **New global `static Instance` singletons.** Inject dependencies.
- **Per-frame `FindObjectsByType` in Update/FixedUpdate.** Cache or maintain a static registry.
- **`Vector3.up` in physics or movement code.** Use `SphereWorld.GetGravityUp(position)`.
- **Trusting client-supplied vectors in `ServerRpc`.** Validate range and clamp magnitude.
- **`Instantiate`/`Destroy` for `NetworkObject`s.** Use `Spawn`/`Despawn` on the server.
- **Procedural Canvas growth in `FriendSlopUI`.** Once the per-screen split lands, new screens get their own builder + class.
- **Adding to a file already over 400 lines.** Split first.

## Open questions

- **NGO scene-transition idiom.** The exact handoff between `RoundManager` (gameplay-phase owner) and `NetworkSceneTransitionService` (scene-load owner) needs to be settled before D-005 ships. Likely answer: `RoundManager` requests, the service executes, `RoundManager.OnNetworkSpawn` re-runs in the new scene to re-anchor.
- **Late-join during transition.** What does a client see if it joins while the server is mid-load? Probably: hold them in a "Joining" UI state, let NGO sync them to the destination scene.
- **Persistent player state across scenes.** `NetworkFirstPersonController` is currently scene-local. For multi-scene, either the player NetworkObject lives in Bootstrap (DontDestroyOnLoad-able NetworkObject), or it gets re-spawned each transition with serialized state passed through `RoundManager`. Pending decision.

## Related documents

- [SpaceshipSceneManagement.md](SpaceshipSceneManagement.md) — current vertical slice, target scene layout, and scene-ownership rules.
- [FeatureIntegrationContracts.md](FeatureIntegrationContracts.md) — feature extension points and PR contracts for AI agents.
- [RemainingFeatures.md](RemainingFeatures.md) — feature backlog organized by system.
- [MultiplayerQA.md](MultiplayerQA.md) — manual playtest checklist.
- [builder-audit.md](builder-audit.md) — GUID-determinism analysis of the editor builders.
- [itch-cicd.md](itch-cicd.md) — CI/CD setup for itch.io deploys.
- [`Space Game/CLAUDE.md`](../Space%20Game/CLAUDE.md) — agent-facing rules derived from this document.

## Roadmap (current)

1. Guardrail docs and CLAUDE.md update. **(Done — this commit.)**
2. Asmdef split (D-006).
3. Make every `BuildXPrefab` load-or-create (see [builder-audit.md](builder-audit.md) recommendations 1–2). Then carve `FriendSlopSceneBuilder` into per-system builders; same outputs, smaller files.
4. `NetworkFirstPersonController` and `FriendSlopUI` split by responsibility.
5. Bug fixes from the initial review (`OnDestroy` override in `NetworkFirstPersonController`, server-side impulse clamps on pickup/drop/throw RPCs, `LaunchpadZone` boarded-set staleness across phase changes, duplicate Start/Restart RPC, `RoundManager.Awake` setting `Instance` before spawn, narrow the `HostOnline` exception filter).
6. Multi-scene split (D-005) — actually create `Bootstrap`, `ShipInterior`, `Planet_StarterJunk` scenes, register in `GameSceneCatalog`, route host startup through `NetworkSceneTransitionService`, ship the multi-client transition test.
7. "Fly to next planet" actually flies (consumes #6).
8. Backfill objective and phase-transition tests.

Each step is independently shippable.
