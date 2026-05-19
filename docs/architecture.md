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

**Status (2026-05-13).** Largely landed for current tier 1–3 content.

- `Bootstrap` (`FriendSlopPrototype.unity`), `ShipInterior.unity`, and the authored planet scenes exist as separate assets and are registered in Build Settings via `MainGameSceneCatalog`.
- `NetworkSceneTransitionService` is consumed by `RoundManager`/`PlanetSceneOrchestrator` through spawn-time dependency wiring. `GameSceneCatalog` is the runtime scene-lookup surface.
- Interior scenes are loaded additively on demand by `InteriorEntrance` / `InteriorSceneBootstrapper` (see D-009).
- Host-side ship lobby → active planet → success → ship return is covered by PlayMode smoke. **Still missing:** a true multi-client transition test, and late-join coverage for each phase (lobby, ship-only, ship+planet active).
- Tier 2 mission variants (`Cobalt Trench`, `Volt Foundry`, `Wraith Halo`) intentionally share `Planet_RustyMoon.unity` as scene owner until a playtest needs unique visuals; see [SpaceshipSceneManagement.md](SpaceshipSceneManagement.md).

Original gating criteria for "complete":

1. `Bootstrap`, `ShipInterior`, and several `Planet_*` scenes exist as separate assets and are in Build Settings. — **Done** (`ShipInterior.unity`, `Planet_StarterJunk.unity`, `Planet_RustyMoon.unity`, `Planet_VioletGiant.unity`, `Planet_IcePlanet.unity`, `Planet_HillsAndValleys.unity`, `Planet_DeepHaul.unity`, `Planet_GhostShift.unity`, `Planet_QuickStrike.unity`, plus `TestWorld_Showcase.unity` and `Building_Interior.unity`).
2. `GameSceneCatalog` is the only place runtime code looks up scenes (no string literals). — **Done.**
3. A multi-client PlayMode test exercises ship → planet → ship transitions and asserts player/round state at each step. — **Pending** (host-side smoke shipped 2026-05-08; multi-client variant still pending).
4. Late-join is tested for each phase (lobby, ship-only, ship+planet active). — **Pending.**

### D-006: Asmdef boundaries

**Decision.** Split runtime code into asmdefs that enforce the dependency graph:

```
FriendSlop.Core  <-  { FriendSlop.Networking, FriendSlop.SceneManagement }  <-  FriendSlop.Gameplay  <-  FriendSlop.UI  <-  FriendSlop.Editor (editor-only)
```

`Core` holds pure data and utilities (`SphereWorld`, `RoundStateUtility`, `JoinCodeUtility`, `CarrySyncUtility`, `RoundPhase`, `ShipPartType`) and has minimal Unity dependency. `Networking` holds the NGO session/Relay/Lobby/Auth/transport layer. `SceneManagement` holds the NGO additive-scene transition service. `Gameplay` holds every NetworkBehaviour (round, player, loot, hazards, ship, interiors, effects, interaction). `UI` may read `Networking`/`Gameplay` but never the reverse. `Editor` is editor-only and depends on everything (it generates the scene).

**Deviation from the original single-"Networking" box (accepted).** This decision originally drew one infra assembly between Core and Gameplay. In practice `Scripts/SceneManagement/` is independently clean — no `using FriendSlop.*` outbound, no inbound from session/transport code — so it ships as its own assembly `FriendSlop.SceneManagement` parallel to `FriendSlop.Networking`. The two infra leaves have no mutual edge, both depend only on Core, and splitting is strictly safer/clearer than fusing.

**Bootstrapper placement (composition root).** `PrototypeNetworkBootstrapper.{cs,Spawning.cs}` lives at `Scripts/Session/` in the `FriendSlop.Gameplay` assembly even though its `namespace` is `FriendSlop.Networking`. It holds typed serialized references to gameplay prefabs (`RoundManager`, `NetworkLootItem[]`, `RoamingMonster`) and reads gameplay state (`PlanetEnvironment.Registered`, `ShipEnvironment.Registered`, `NetworkFirstPersonController.ActivePlayers`, `RoundManagerRegistry`); placing it in the Networking assembly would create a Networking→Gameplay edge and break the layered direction. The folder/namespace/assembly mismatch is the deliberate price of keeping Networking clean; Unity resolves the type by script GUID + assembly graph regardless of namespace, so consumers `using FriendSlop.Networking;` still compile. An optional follow-up to rename the namespace to `FriendSlop.Session` is tracked as out-of-scope cosmetic cleanup.

**Steam-readiness.** `FriendSlop.Networking` is the **swap surface** for the eventual Steamworks migration (Steam Lobby/Friends/Relay/SteamNetworkingSockets replacing UGS Relay/Lobby/Auth and UTP). Nothing outside this assembly references `Unity.Services.*` types, so the eventual swap is a self-contained change to one assembly's implementation + UPM packages, with `Gameplay`/`UI`/`Editor` untouched. Discipline going forward: keep `FriendSlop.Networking`'s public API backend-neutral — no UGS types (e.g. `Unity.Services.Lobbies.Models.Lobby`) in method signatures or `NetworkVariable<T>` payloads crossing the boundary. If `NetworkSessionManager` currently exposes UGS types to Gameplay (e.g. via the event subscription in `Scripts/Round/FlatTestWorldEnvironment.cs`), neutralizing those public surfaces is a known Steam-swap follow-up — handle in the Steam migration PR, not piecemeal.

**Why.** asmdefs are the only mechanism in Unity that physically prevent a wrong-direction dependency. Once enforced, AI agents can't accidentally couple UI logic into gameplay or vice versa, and the Steam swap stays contained to one assembly. `ArchitectureGuardrailTests.AsmdefReferencesEnforceLayeredDirection` enforces the full graph at CI.

**Cost.** A handful of `.asmdef` files and some namespace cleanup. One-time pain, permanent payoff.

**Status (2026-05-19).** Complete. `FriendSlop.Runtime` was renamed to `FriendSlop.Gameplay` (preserving the asmdef `.meta` GUID `8489b6e9b3258b4419528517c0c0048b` so by-GUID refs survive); `FriendSlop.Networking` and `FriendSlop.SceneManagement` were carved out as independent infra assemblies; the bootstrapper was relocated to `Scripts/Session/`; UI/Editor/EditModeTests/PlayModeTests asmdef refs were rewritten to the new graph; the guardrail test was rewritten to enforce the full direction. Headless-validated (EditMode 147/147, PlayMode green).

### D-007: File-size and singleton limits

**Decision.** Files stop at ~400 lines. New project-owned singleton-style globals are refused by default. See D-014 for the singleton policy and migration history.

**Why.** Both are leading indicators that the code is growing in the wrong shape. AI agents naturally append to existing files and reach for global state. A hard cap forces the right reaction (split, inject) at the moment when the cost is still small.

**Cost.** Some refactor churn. Worth it.

**Status (2026-05-13).** Earlier oversized files (`FriendSlopUI`, `NetworkFirstPersonController`, `NetworkLootItem`, `PlayerInteractor`, `RoundManager`, `NetworkSessionManager`) have all been carved below the default cap and removed from the baseline. Current `ArchitectureGuardrailTests.ExistingOversizedRuntimeFiles` baseline (all Interiors, carried over from the interiors merge):

| File | Line count | Split notes |
|---|---|---|
| `Assets/Scripts/Interiors/InteriorLayoutGenerator.cs` | 1727 | Natural seams: required-room quotas, frontier expansion, downward-connector mirroring, fallback generation. |
| `Assets/Scripts/Interiors/Blueprints/BlueprintEditorUI.cs` | 1005 | Editor-only; per-panel split. |
| `Assets/Scripts/Interiors/InteriorSceneBootstrapper.cs` | 832 | Natural extraction: wall-patching / garage-door geometry, then materials, then door spawning. |
| `Assets/Scripts/Interiors/Blueprints/BlueprintEditorController.cs` | 545 | Editor-only; smallest, safest first split. |
| `Assets/Scripts/Interiors/InteriorSceneBootstrapper.Furniture.cs` | 527 | Extract pickers (`PickFurnitureForAnchor`, `HasTagOverlap`, etc.) into `.Furniture.Picking.cs`. |

Each entry stays in the baseline until the main file lands under 400; drop the entry in the same PR that does the split.

### D-008: Tests at architectural seams

**Decision.** EditMode tests cover pure-logic helpers (already done for `JoinCodeUtility`, `RoundStateUtility`, `CarrySyncUtility`). Going forward, every new `RoundObjective` subclass requires `Evaluate` tests; phase transitions in `RoundManager` get a state-machine test; multi-scene transitions get a multi-client PlayMode test. The PlayMode smoke test stays as a top-level "the prototype boots" check.

**Why.** With no editor-driven verification, tests are the only thing standing between a vibe-coded change and a silently broken playtest. The seams (objective evaluation, phase transitions, scene transitions, RPC validation) are where logic goes wrong invisibly.

**Cost.** Slower per-feature delivery. Bug-discovery time drops dramatically.

### D-009: Interior generation is data-driven content

**Decision.** Interior layouts are produced by feeding ScriptableObject definitions (`BuildingDefinition` + `RoomDefinition` + `FurnitureDefinition`, registered in `InteriorCatalog`) into a deterministic generator (`InteriorLayoutGenerator.Generate(definition, seed)`). The server picks the seed and writes it into `InteriorSessionData`; every client regenerates the same layout locally from that seed. Doors are `NetworkObject`s (open/close state syncs); furniture is deterministic-but-local (no `NetworkObject` per chair, every client picks identical pieces from the seed).

**Why.** Procedural interiors without a hard determinism contract would either need every chair to be a `NetworkObject` (bandwidth/spawn-count blowup) or accept that clients see different rooms (correctness disaster). Pinning the contract to "server picks seed, all clients regenerate locally" gets us identical state without the network cost, *as long as* layout/furniture selection is a pure function of the seed. ScriptableObject-first content keeps the door open for blueprint authoring (D-010) and per-room variant pooling.

**Cost.** Every line of generation code is on the path of the determinism contract. Any `Random` source, time stamp, or `FindObjectsByType` order dependence in generation is a correctness bug, not a style nit. Tests have to cover the pipeline both for output shape (rooms placed, constraints satisfied) and for stability under future renames — see the `FormerlySerializedAs` hazards in section 16c of the backlog.

**Status (2026-05-13).** Pipeline is live: `InteriorEntrance` → `InteriorSessionData` → `InteriorSceneBootstrapper` → `InteriorLayoutGenerator` → spawned doors + local furniture. `InteriorLayoutGeneratorTests` covers a subset of the generator; `BlueprintLayoutBuilderTests`, `FurnitureSelectionTests`, `BuildingDefinitionRoomPoolTests`, and an `InteriorSceneBootstrapper` PlayMode smoke are queued in BACKLOG section 16c.

### D-010: Blueprints as authored interior layouts

**Decision.** When a building's interior should be hand-authored rather than rolled, the entry point is a `BlueprintAsset` that pins room placements and edge state (`Wall` / `Open` / `Door`) explicitly. `InteriorSessionData.Blueprint` overrides the procedural path: `BlueprintLayoutBuilder.Build(blueprint, definition)` produces the `InteriorLayout` directly, skipping the procedural door-policy pass because edge state is user-authored. Per-slot room variants (`Room_Residential_Bathroom_2x2.A` / `.B` etc., resolved via `RoomVariants.FindVariants` on family name + grid size) are still rolled per-spawn for variety without re-generating the layout.

**Why.** Procedural is great for "generic dungeon"; it's wrong for places the design wants to be recognisable (the residential building, the friend's homebase). Going through the same `InteriorLayout` shape means the bootstrapper, door spawner, and furniture pipeline don't have to fork.

**Cost.** A second code path with its own builder, editor (`BlueprintEditorController` / `BlueprintEditorUI`), and runtime entrance variant (`BlueprintEntrance`). `BlueprintLayoutBuilder` had zero coverage at the time this decision was recorded — see BACKLOG 16c for the queued `BlueprintLayoutBuilderTests`.

**Status (2026-05-13).** Live. Used by the residential test building. Editor flow is in `Scripts/Interiors/Blueprints/` and is editor-only; the runtime builder is small (~80 lines) and pure.

### D-011: NGO scene placement contract

**Decision.** When spawning a `NetworkObject` into a scene other than the caller's, set the active scene to the target *before* `Instantiate` + `NetworkObject.Spawn(destroyWithScene: true)`. The canonical pattern is an active-scene swap around the whole spawn batch — see `ActiveSceneScope` in [`PrototypeNetworkBootstrapper.Spawning.cs`](../Space%20Game/Assets/Scripts/Networking/PrototypeNetworkBootstrapper.Spawning.cs) and the inlined equivalent in [`PlanetLootSpawner.TrySpawnNow`](../Space%20Game/Assets/Scripts/Loot/PlanetLootSpawner.cs). Do **not** rely on `SceneManager.MoveGameObjectToScene` after `Spawn`. When calling `Object.FindObjectsByType<T>(FindObjectsInactive.Include, …)` against an NGO type, filter by `NetworkObject.IsSpawned` if you care about live runtime state.

**Why.** `NetworkObject.Spawn(destroyWithScene: true)` latches `SceneOriginHandle` to the GameObject's scene at the moment of spawn. With `EnableSceneManagement = true`, post-Spawn `MoveGameObjectToScene` calls fight NGO scene management and silently fail to stick (cost ~2 hours of debug time during PR #24). Separately, NGO parks `NetworkPrefabsList` templates as inactive instances in the Bootstrap scene; unfiltered `FindObjectsByType` returns them alongside real spawns, so unfiltered counts are non-zero before any real spawn fires.

**Cost.** Every new spawn site has to use the pattern; tests that assert spawn counts have to filter. Codified as hard rule 9 in [`Space Game/CLAUDE.md`](../Space%20Game/CLAUDE.md) and detailed in [NetworkObjectSceneOwnership.md](NetworkObjectSceneOwnership.md).

**Status (2026-05-13).** Codified. Known stale sites: `MeteorShower.SpawnMeteor` (still uses post-Spawn move; queued in BACKLOG 16b) and `AnomalySpawner.Spawn` (still defaults `destroyWithScene = false`; queued in BACKLOG 16b).

### D-012: Third-party assets are quarantined and import-once

**Decision.** Asset Store / third-party packs do **not** live in `Assets/<PackName>/` on the feature path. Each pack goes in one quarantine root — `Assets/ThirdParty/<PackName>/`, or an embedded UPM package under `Packages/` when the pack is package-shaped — wrapped in its own `.asmdef` (`ThirdParty.<PackName>`, `autoReferenced` only when our runtime must call into it). A pack is imported **exactly once**, in a dedicated PR that does nothing else (`vendor: add <Pack> vX`). Demo / example / sample-scene folders inside the pack are deleted on import. If the pack adds a new binary extension, `.gitattributes` LFS coverage is extended in that same PR. Feature branches never import, re-import, or re-export a pack — they only reference one that already landed.

**Why.** Quantified on `main` (2026-05-15): ~12,200 of the repo's files are vendor packs — `LowPolyInterior2` alone is 9,006 files, `LowPolyInterior` 1,714, `Plugins/Microdetail` 874, `HIVEMIND` 471 — versus ~360 for our own `Prefabs/`. When a pack sits on the feature path and gets re-imported per branch, every feature branch diffs by 1M+ lines, branches collide on vendor `.meta`/YAML, review becomes impossible, `.git` bloats (978 MB), and LFS bandwidth spikes (the direct cause of the [#25](https://github.com/TheSchlote/Friend-Slop/pull/25)/[#26](https://github.com/TheSchlote/Friend-Slop/pull/26) cost firefight). Quarantine + import-once keeps a feature PR reviewable as a feature PR, and an own-asmdef stops our code from coupling to a pack's churn.

**Cost.** A one-time relocation of the packs already on `main` (staged in BACKLOG §17) and the discipline of the dedicated import PR. asmdef-wrapping a pack occasionally needs a few reference / `.asmref` fix-ups. One-time pain, permanent payoff.

**Status (2026-05-18).** Policy in force and **executed**. `LowPolyInterior`, `LowPolyInterior2`, `_Recovery` dropped (zero references); `HIVEMIND`, `Plugins/Microdetail`, `YughuesFreeRockMaterials` relocated to `Assets/ThirdParty/<Pack>/`, each wrapped in its own `autoReferenced:false` `ThirdParty.<Pack>` asmdef (Microdetail also has two nested editor asmdefs), all headless-validated (EditMode 137/137, zero regression). No `FriendSlop.*` → vendor asmdef edge exists — vendor is GUID/asset-wired, not code-wired. Remaining: only the optional `.git`/LFS history purge (BACKLOG §17d, destructive + separately gated). The rule stands: never add a new pack under `Assets/<root>`; never re-import an existing one on a feature branch.

### D-013: Short-lived branches, rebased, vendor imports isolated

**Decision.** Feature branches are short-lived (target: merged within a few days), rebased on `main` before review rather than left to diverge, and scoped to one feature. A branch never carries a vendor-pack import alongside feature code — the import is its own prior PR (D-012). The lead may keep a single integration branch *only* for reconciling already-merged history; it is not a place to accumulate features. Contributor flow: branch off fresh `main` → add the feature as C# + `.asset` files (D-001/D-004/D-008) → rebase → PR. If the feature needs a new art pack, the D-012 pack PR lands first and the feature branch just references it.

**Why.** The three friend branches (`interiors-changes`, `interior-variety`, `procedural-world-generation-tier-4-test`) are overlapping long-lived re-cuts of the same work, each re-importing the same packs; all are ~6 commits behind `main`, mutually conflicting (13–15 files), and effectively unmergeable. The cause is divergence over time plus vendor churn, **not** the feature code itself — there is only ~3.8K–14K lines of real gameplay across them, which is salvageable. Short-lived rebased branches with vendor factored out keep every contribution mergeable, which is the whole point of "let the friend keep adding features."

**Cost.** The contributor rebases and keeps branches narrow instead of living in one long-running branch. The time saved not resolving million-line conflicts dwarfs it.

**Status (2026-05-15).** Policy set. Existing divergent branches are reconciled per BACKLOG §17e (salvage real code onto fresh branches off `main`, drop the vendor noise). `merge/all-branches-to-main` is the only friend-work line that currently merges cleanly (0 conflicts vs `main`) and is the reference for "already integrated."
### D-014: No project-owned singletons

**Decision.** No project-owned `Instance` / `LocalPlayer` static globals. Cross-cutting state is reached through narrow registries (`RoundManagerRegistry`, `LocalPlayerRegistry`, `DayNightCycleRegistry`), spawn-time wiring (`SerializeField`, parameters at `Spawn()` time), or events. `ArchitectureGuardrailTests.NoNewSingletonStyleGlobals` enforces this — there are zero approved project-owned globals.

**Why.** "Trust the static" produces invisible coupling: callers depend on `RoundManager.Instance` instead of declaring a real dependency, and tests can't substitute a stub. Registries make the dependency explicit (the caller asks "is there a current round?") without forcing every consumer to be wired at spawn time.

**Cost.** A handful of registry classes plus a small lifecycle discipline (each owner self-registers in `OnNetworkSpawn`/`OnEnable` and unregisters in `OnNetworkDespawn`/`OnDisable`).

**Status.** Migration complete. History:

| Original global | Replacement | Reason |
|---|---|---|
| `FriendSlopUI.Instance` | Removed | UI was using itself as a lookup shortcut. Gameplay input blocking now goes through `GameplayInputState`; UI session calls use a cached `NetworkSessionManager` reference. |
| `NetworkSessionManager.Instance` | Removed | The manager is a scene-owned component on the `NetworkManager` object. UI and tests can hold or discover that component directly. |
| `NetworkSceneTransitionService.Instance` | Removed | Scene-owned infrastructure. `PrototypeNetworkBootstrapper` passes it into the spawned `RoundManager`, which passes it to `PlanetSceneOrchestrator`. |
| `RoundManager.Instance` | `RoundManagerRegistry.Current` | Networked `RoundManager` self-registers in `OnNetworkSpawn` / `OnNetworkDespawn`. Callers depend on the registry instead of a static facade. |
| `NetworkFirstPersonController.LocalPlayer` | `LocalPlayerRegistry.Current` + `LocalPlayerChanged` event | The local player self-registers when ownership becomes local; UI subscribes to the change event instead of polling. |

**Best-practice guidance for new code.**

- Prefer `[SerializeField]` references for same-scene dependencies.
- Prefer spawn-time configuration for runtime-spawned objects.
- Prefer events or small registry/provider components when many systems need the same state.
- Do not use a singleton to avoid wiring a reference.
- If a dependency is optional, resolve it once and cache it; do not search every frame.

## Anti-patterns to refuse

The following are explicitly out of scope for this project. If you find yourself reaching for one, stop and propose an alternative.

- **Hand-editing prefab/scene YAML for gameplay configuration.** Use a builder.
- **New global `static Instance` singletons.** Inject dependencies.
- **Per-frame `FindObjectsByType` in Update/FixedUpdate.** Cache or maintain a static registry.
- **`Vector3.up` in physics or movement code.** Use `SphereWorld.GetGravityUp(position)`.
- **Trusting client-supplied vectors in `ServerRpc`.** Validate range and clamp magnitude.
- **`Instantiate`/`Destroy` for `NetworkObject`s.** Use `Spawn`/`Despawn` on the server.
- **`SceneManager.MoveGameObjectToScene` after `NetworkObject.Spawn`.** Set the active scene before Instantiate + Spawn instead (D-011).
- **Defaulting `destroyWithScene: false` on `NetworkObject.Spawn`.** Sends the object to `DontDestroyOnLoad`, which leaks it across planet transitions.
- **Unfiltered `Object.FindObjectsByType<T>` over NGO types.** `NetworkPrefabsList` templates show up as inactive `IsSpawned=false` instances; filter on `NetworkObject.IsSpawned` (D-011).
- **Procedural Canvas growth in `FriendSlopUI`.** New large screens get their own component/partial under `Scripts/UI/` (see `FriendSlopUI.TestMode.cs`); do not append another section to the existing partials.
- **Adding to a file already over 400 lines.** Split first.
- **Importing an Asset Store pack into `Assets/<PackName>/`.** Quarantine under `Assets/ThirdParty/` (or an embedded `Packages/` package), own asmdef, dedicated import-once PR (D-012).
- **Bundling a vendor-pack import with feature code, or re-importing a pack on a feature branch.** The pack lands once in its own PR; feature branches only reference it (D-012/D-013).
- **Long-lived feature branches that diverge from `main`.** Short-lived, rebased, one feature per branch (D-013).

## Open questions

- **NGO scene-transition idiom.** The first handoff is settled: `PrototypeNetworkBootstrapper` passes `NetworkSceneTransitionService` into the spawned `RoundManager`, and `RoundManager` delegates additive planet loads to `PlanetSceneOrchestrator`. Late-join behavior and full ship/planet scene splitting still need tests.
- **Late-join during transition.** What does a client see if it joins while the server is mid-load? Probably: hold them in a "Joining" UI state, let NGO sync them to the destination scene.
- **Persistent player state across scenes.** `NetworkFirstPersonController` is currently scene-local. For multi-scene, either the player NetworkObject lives in Bootstrap (DontDestroyOnLoad-able NetworkObject), or it gets re-spawned each transition with serialized state passed through `RoundManager`. Pending decision.

## Related documents

- [SpaceshipSceneManagement.md](SpaceshipSceneManagement.md) — current vertical slice, target scene layout, and scene-ownership rules.
- [NetworkObjectSceneOwnership.md](NetworkObjectSceneOwnership.md) — D-011 in full: `ActiveSceneScope` pattern, `IsSpawned` filter, NGO scene-management gotchas.
- [InteriorSystem.md](InteriorSystem.md) — D-009/D-010 in full: building/room/furniture data, procedural vs. blueprint paths, what's networked vs. local.
- [FeatureIntegrationContracts.md](FeatureIntegrationContracts.md) — feature extension points and PR contracts for AI agents.
- [BACKLOG.md](../BACKLOG.md) — current ordered feature/engineering backlog with grooming questions and decision options.
- [MultiplayerQA.md](MultiplayerQA.md) — manual playtest checklist.
- [builder-audit.md](builder-audit.md) — GUID-determinism analysis of the editor builders.
- [itch-cicd.md](itch-cicd.md) — CI/CD setup for itch.io deploys.
- [`Space Game/CLAUDE.md`](../Space%20Game/CLAUDE.md) — agent-facing rules derived from this document.

## Roadmap (current)

> **§17 vendor quarantine substantially complete (2026-05-18).** Unreferenced packs dropped and kept packs relocated under `Assets/ThirdParty/` with their own `ThirdParty.*` asmdefs (`HIVEMIND`, `Microdetail` + 2 nested editor asmdefs, `YughuesFreeRockMaterials`), all headless-validated (EditMode 137/137, zero regression). **The numbered roadmap below is active again.** Only the *optional, destructive, separately-gated* `.git`/LFS history purge (BACKLOG §17d) and the friend-branch reconciliation (handled via the collaborator merge workflow when the friend is ready — not an agent-initiated roadmap item) remain; neither blocks the roadmap.

1. Guardrail docs and CLAUDE.md update. **Done.**
2. Asmdef split (D-006). **Done** — `FriendSlop.Runtime` carved into `FriendSlop.Gameplay` + `FriendSlop.Networking` + `FriendSlop.SceneManagement`; bootstrapper relocated to `Scripts/Session/` to keep the Networking assembly clean; full graph enforced by `ArchitectureGuardrailTests.AsmdefReferencesEnforceLayeredDirection`. `FriendSlop.Networking` is the documented Steam-swap surface.
3. Make every `BuildXPrefab` load-or-create (see [builder-audit.md](builder-audit.md) recommendations 1–2). **Done.** Then carve `FriendSlopSceneBuilder` into per-system builders; same outputs, smaller files. *In progress.*
4. `NetworkFirstPersonController` and `FriendSlopUI` split by responsibility. **Done** — both now under the 400-line cap.
5. Bug fixes from the initial review (`OnDestroy` override in `NetworkFirstPersonController`, server-side impulse clamps on pickup/drop/throw RPCs, `LaunchpadZone` boarded-set staleness across phase changes, duplicate Start/Restart RPC, `RoundManager.Awake` setting `Instance` before spawn, narrow the `HostOnline` exception filter). **Done.**
6. Multi-scene split (D-005) — `Bootstrap`, `ShipInterior`, and per-planet scenes exist, are registered in `MainGameSceneCatalog`, and host startup routes through `NetworkSceneTransitionService`. **Mostly done** — host-side smoke ships; multi-client transition test still pending.
7. "Fly to next planet" actually flies (consumes #6). **Done.**
8. Backfill objective and phase-transition tests. **Done** — objective `Evaluate` coverage for `QuotaObjective`/`RocketAssemblyObjective` ([#36](https://github.com/TheSchlote/Friend-Slop/pull/36)) on top of the extraction-ready/objective-text suites ([#35](https://github.com/TheSchlote/Friend-Slop/pull/35)); the `RoundManager` phase machine is `IsServer`-gated, so its testable seam — the final-tier expedition latch — was extracted to the pure `RoundStateUtility.RecordFinalTierSuccess` and EditMode-covered ([#38](https://github.com/TheSchlote/Friend-Slop/pull/38)). The broader `IsServer`-gated machine (Loading→Active gate, all-dead) is left for a PlayMode NGO harness if it ever earns its keep.
9. Final-tier ending and run-loop rules per [BACKLOG.md](../BACKLOG.md) §3 — pick A/B/C/D and wire the final scene flow.
10. Tier 2 planet identity per [BACKLOG.md](../BACKLOG.md) §2 — graduate variants from the shared Rusty Moon owner one at a time as their dedicated scenes get unique content.

Each step is independently shippable.
