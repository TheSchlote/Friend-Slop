# Friend Slop Backlog

This is the current gameplay and implementation backlog, ordered by the work that most affects future feature development. Prefer grooming the top items before adding more planet or objective content.

Each open-design section lists the concrete options on the table and a recommended choice. When a decision lands, replace the recommendation with the chosen approach + rationale, and update [docs/architecture.md](docs/architecture.md) if the decision changes architecture. See the **Decision dependencies** appendix at the bottom for which decisions unblock the most downstream work.

## 1. Planet lifecycle and scene ownership

Authored planets have moved into additively loaded planet scenes, but lifecycle ownership still needs hardening around bootstrap/ship responsibilities, travel cleanup, and future planet variants. Keep the transition moving so each playable planet owns its scene, environment, launchpad, teleporters, spawns, loot budget, hazards, and cleanup rules.

Status 2026-05-03: Starter Junk, Rusty Moon, and Violet Giant are now scene-owned. Rusty Moon exposes loot and monster anchors through `PlanetEnvironment`, Violet Giant has moved out of `FriendSlopPrototype.unity`, and validation now rejects scene-owned `PlanetEnvironment`s left nested in the bootstrap scene. `ShipInterior.unity` now owns the ship lobby root, ship stations, ship teleporter, and ship spawn points through `ShipEnvironment`; `FriendSlopPrototype.unity` stays focused on bootstrap/runtime systems. Planet travel cleanup is scoped to the active planet scene when a `PlanetEnvironment` is registered, with the legacy global cleanup path retained for fallback planets. Current-round and local-player lookup now flow through explicit registries instead of production code calling `RoundManager.Instance` or `NetworkFirstPersonController.LocalPlayer`. Keep the bootstrapper legacy planet fallback fields until they get a dedicated removal pass.

Status 2026-05-08: PlayMode smoke now covers the host-side ship lobby -> active planet -> success return-to-ship flow. The true multi-client version of this transition test is still pending.

Status 2026-05-14: dedicated planet scenes now exist for `Planet_IcePlanet`, `Planet_HillsAndValleys`, `Planet_DeepHaul`, `Planet_GhostShift`, and `Planet_QuickStrike`, alongside `Planet_StarterJunk`, `Planet_RustyMoon`, and `Planet_VioletGiant`. A `TestWorld_Showcase` scene and a `Building_Interior` procedural-interior scene have also been added; `RoundManagerRegistry.Current` and `LocalPlayerRegistry.Current` are now the only runtime entry points for those references.

Key files:
- `Space Game/Assets/Scripts/Round/RoundManager.cs`
- `Space Game/Assets/Scripts/Round/RoundManagerRegistry.cs`
- `Space Game/Assets/Scripts/Player/LocalPlayerRegistry.cs`
- `Space Game/Assets/Scripts/Session/PrototypeNetworkBootstrapper.cs`
- `Space Game/Assets/Scripts/Round/PlanetEnvironment.cs`
- `Space Game/Assets/Scripts/Ship/ShipEnvironment.cs`

Grooming questions:
- Which remaining bootstrap scene objects should move into dedicated runtime/persistent scenes?
- Should any planets remain nested in `FriendSlopPrototype.unity`?

Options for carried/deposited loot across travel:
- **A.** Deposited persists; carried is lost. Strong incentive to deposit before flying. Punishing for last-second extraction.
- **B.** Both persist. Easiest to implement; risks oversized inventories crossing planets.
- **C.** Deposited persists; carried converts to cash on travel. Removes hoarding without punishing time-out players.
- **D.** Both persist, but the ship has a finite cargo bay. Hard cap that leans into "salvage" framing.

Recommendation: **C** for the first iteration (simple, fair, supports the deposit zone's purpose). **D** as a stretch goal if ship customization adds a cargo-bay upgrade.

## 2. Tier 2 planet identity

Several tier 2 planets are presented as different destinations but currently share Rusty Moon's scene/runtime environment. As of 2026-05-14 each variant has its own dedicated scene (`Planet_DeepHaul`, `Planet_QuickStrike`, `Planet_GhostShift`), but those scenes are still placeholder copies of Rusty Moon. Decide whether to invest unique authoring per variant or roll the placeholder scenes back and frame them as mission variants on Rusty Moon.

Key files:
- `Space Game/Assets/Planets/Tier2_*.asset`
- `Space Game/Assets/Scenes/Planet_DeepHaul.unity`, `Planet_QuickStrike.unity`, `Planet_GhostShift.unity`, `Planet_RustyMoon.unity`

Grooming questions:
- Are Cobalt Trench, Volt Foundry, Wraith Halo, and Rusty Moon unique places or mission variants?
- Should each variant have unique lighting, loot, hazards, and objective copy?

Options:
- **A. Mission variants on the same scene.** Roll the placeholder scenes back; each tier 2 keeps a distinct `PlanetDefinition` + `RoundObjective` but shares Rusty Moon's environment. Cheap, ships fast. Risk: visually monotone.
- **B. Unique planets, unique scenes.** Each tier 2 gets unique geometry, lighting, hazards. Aligns with the "one planet per scene" target. Cost: ~5 scenes' worth of authoring + roughly one PR per planet.
- **C. Hybrid.** Two or three "marquee" tier 2 planets get unique scenes (Cobalt Trench = water/cave, Volt Foundry = industrial), the rest stay as "Rusty Moon: [variant name]" return contracts.

Recommendation: **C**. Pick two visually distinct marquee planets, frame the rest as named return contracts. Visual variety where it counts; manageable authoring cost.

## 3. Progression ending and loop rules

`PlanetCatalog.MaxTier` supports up to tier 10, but authored content currently reaches tier 4. Define the win state, run loop, replay behavior, and whether final-tier success ends the expedition or keeps cycling.

Status 2026-05-08: runtime final-tier detection now uses the highest authored tier in the active `PlanetCatalog`, not the placeholder `PlanetCatalog.MaxTier`. Final-tier success records a completed expedition, shows expedition-complete UI copy, and the host action returns the session to the tier-1 ship lobby instead of replaying the final planet.

Status 2026-05-14: catalog now reaches tier 4 with `Tier4_HillsAndValleys`. A new tier 3 destination (`Tier3_IcePlanet`) has been added alongside Violet Giant, and a `Flat Test World` (catalog tier 10) is reachable via the host's lobby Test Mode for prefab/asset showcasing.

Key files:
- `Space Game/Assets/Scripts/Round/PlanetCatalog.cs`
- `Space Game/Assets/Scripts/Round/RoundManager.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Menu.cs`

Grooming questions:
- What is the actual end of a run?
- Should final-tier success show credits, return to lobby, or start a harder loop?
- How should failed/all-dead states affect planet progression?

Options:
- **A. Run-based (rogue-like).** Final-tier success → credits → return to lobby with a session score. Each session starts from tier 1. Optional persistent unlocks (cosmetics, ship modules) carry between runs.
- **B. Endless cycle.** Final-tier success loops back to tier 1 with a difficulty multiplier. The "ending" is whenever the crew dies out. Score-attack framing.
- **C. Authored campaign with epilogue.** Tier 10 has unique narrative content; success ends the expedition; failure → game over. Fixed-length experience.
- **D. Open-ended sandbox.** Pick which planet to fly to from a star map; no forced tier progression; "win" by hitting a money goal.

Recommendation: **A (run-based)**. Fits a co-op salvage game's repeat-play loop best, lets you ship with tier 4 content while the loop still feels complete, and the "return to lobby" beat is already what `RoundManager` does. **B** is a strong follow-up once the loop is fun. Failure handling: lose the run on All-Dead; keep tier on partial-fail (Quota miss with surviving players).

## 4. Ship stations

Ship stations currently support claiming/releasing occupancy, but station roles do not open real flows yet. Pilot, holographic board, module slot, customization bench, and future utility stations need concrete behavior.

Key files:
- `Space Game/Assets/Scripts/Ship/ShipStation.cs`
- `Space Game/Assets/Scenes/ShipInterior.unity`

Grooming questions:
- Which station controls travel?
- Which station previews planet choices?
- What does customization change, and is it cosmetic or gameplay-affecting?

Options per station:
- **Pilot station.** Required-occupant model (recommended): travel only starts when one player presses "Engage" while occupying it. Alternatives: travel as a vote, or travel triggered from anywhere by the host.
- **Holographic board.** Roll-of-three with one re-roll (recommended; already half-built). Alternatives: star map with travel cost; random with no re-roll.
- **Customization bench.** Cosmetic only at first (recommended). Gameplay upgrades (bigger inventory, faster travel) open a balancing rabbit hole; defer until the core loop is fun.
- **Vending / restock.** Single money-driven kiosk (recommended). Sell back unwanted loot through the same money stat as the deposit flow; do not duplicate.

Recommendation: treat the ship as a hub, not a UI replacement. Each station should require physical occupancy to gate co-op coordination (one player drives, another previews, another restocks).

## 5. Objective UX

The original problem statement here ("feedback is thin, success copy still assumes rocket assembly") is **stale** — most of it shipped before this section was last groomed.

Status 2026-05-18 — audited against `main`, mostly done:
- **Persistent primary-objective HUD line: shipped.** `BuildObjectiveHudText` → `objective.BuildHudStatus(round)` (with `Title`/parts fallbacks), wired top-left, refreshed per-frame. All three objectives emit specific live copy.
- **Per-objective success/failure copy: shipped.** `BuildSuccessResultText`/`BuildFailureResultText` delegate to `RoundObjective.BuildSuccessText`/`BuildFailureText`; the rocket-assembly hardcode is gone. Final-tier "expedition complete" + next-planet rolling already present. Covered by `RoundObjectiveTextTests`, `SurvivalObjectiveExtractionTests`.
- **Extraction-ready banner: shipped this change.** New `RoundObjective.IsExtractionReady`/`BuildExtractionBanner` virtuals (overridden per objective: quota-met & boarding pending / rocket assembled & boarding pending / survival extraction window), a centred transient fading+pulsing banner (`FriendSlopUI.ObjectiveBanner.cs`, driven per-frame from `Update()` off server-replicated state, no authority change), and `RoundObjectiveExtractionReadyTests`. Banner visuals (placement/timing/legibility) still need a human playtest.

**Deferred (decision, not a TODO): success/failure text as `RoundObjective` `.asset` `successText`/`failureText` fields.** The computed strings interpolate live values (`$450 / $500`, per-part status); static asset strings cannot without adding a token-templating layer. That is real complexity for low value in a two-person, agent-coded project where humans do not author copy in the inspector. Revisit only if non-engineer copy authoring becomes a real need.

Key files:
- `Space Game/Assets/Scripts/Round/Objectives/*.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.ObjectiveBanner.cs`, `FriendSlopUI.Menu.cs`

Remaining grooming question (open, low priority):
- Should objective state get richer dedicated UI instead of the one compact top-left line + transient banner? (Pairs with §11 prefab move; defer until a screen actually feels cramped in playtest.)

## 6. Teleporter flow

Teleporters are automatic trigger pads. This works, but can feel surprising during launchpad/extraction gameplay. Decide whether teleport should be automatic, interact-to-use, station-controlled, or phase-gated.

Key files:
- `Space Game/Assets/Scripts/Round/TeleporterPad.cs`
- `Space Game/Assets/Scripts/Round/RoundManager.cs`

Grooming questions:
- Should players press a key to teleport?
- Should carrying loot, carrying bodies, or being in extraction state alter teleport rules?
- Should the ship-side teleporter be active during every objective?

Options:
- **A. Keep it automatic.** Trigger pad teleports on contact. Simple; works today.
- **B. Press-to-teleport.** Stand on the pad, hold E, get teleported. Removes accidents.
- **C. Station-controlled.** Only the ship-side pad activates when a crewmate at the right station hits a button. Forces coordination.
- **D. Phase-gated automatic.** Auto-teleport, but only during specific phases (Active and Success on planet → ship; Lobby and Transitioning blocked).

Recommendation: **B (press-to-teleport)** for the planet-side pad with a 1-second hold to confirm; auto for the ship-side pad once the round resolves. Allow teleport with carried loot (the whole point); allow carried bodies but emit a chat log so the rest of the crew sees who came back with whom.

## 7. Loot economy and spawn budgets

Loot quotas are currently tuned by hand. Move toward budgeted loot generation so each mission guarantees a minimum possible value while still allowing rarity variance.

Key files:
- `Space Game/Assets/Scripts/Loot/LootPool.cs`
- `Space Game/Assets/Scripts/Loot/PlanetLootSpawner.cs`
- `Space Game/Assets/Loot/Tier2_LootPool.asset`

Grooming questions:
- What minimum total value should each objective guarantee?
- Should rare items be optional upside or required for success?
- Should loot values scale by tier, crew size, or objective type?

Recommendation: a **per-mission budget model** with a 120% guaranteed minimum and 200% potential ceiling. Each `PlanetDefinition` declares `quotaTarget` (already exists) plus a new `lootValueBudget`; the spawner rolls until total spawned value is in `[1.2 × quotaTarget, 2.0 × quotaTarget]`. Within budget, items come from a `LootPool.asset` rolled by rarity weights. Do not scale by crew size for v1 — too easy to game by ghost-joining.

**Status 2026-05-19: code-side shipped.** `PlanetDefinition.lootValueBudget` (int, default 0) and a pure `LootBudget` helper (Min=1.2×, Max=2.0×, safety cap = 256 rolls) wire `PlanetLootSpawner` into a budget-driven loop: it cycles through `spawnPoints[]`, rolls until cumulative item value reaches the 1.2× floor, and skips individual rolls that would push past the 2.0× ceiling. `lootValueBudget == 0` keeps the legacy `spawnPoints × rollsPerSpawnPoint` behavior intact for every existing planet asset. EditMode-validated via `LootBudgetTests`. **Open follow-up: author `lootValueBudget` per-planet** (Starter Junk, Rusty Moon, Violet Giant, tier 3+) so the budget loop actually activates — currently every shipped `.asset` is at 0 (legacy).

## 8. Combat and item rules

Laser gun and boxing gloves are implemented, but their role in co-op play needs rules. Friendly fire, stun duration, ammo refill, rarity, and grief potential should be decided before adding more weapons.

Key files:
- `Space Game/Assets/Scripts/Loot/LaserGun.cs`
- `Space Game/Assets/Scripts/Loot/BoxingGloves.cs`
- `Space Game/Assets/Scripts/Player/PlayerInteractor.cs`

Grooming questions:
- Is friendly fire intentional?
- Should weapons be loot, shop items, mission tools, or rare chaos items?
- How should ammo/cooldowns reset across rounds and planets?

Options for friendly fire: **A.** On (full PvP). **B.** Off (no damage between crewmates). **C.** Partial (knockback yes, damage no).

Recommendation: **partial FF (C)**. Allows funny moments and tactical pushes without "friend just killed me at the launchpad." Weapons distributed as **loot-only with rarity tiers**; ammo persists with the weapon and refills at a ship vending kiosk (see §4) for cash.

## 9. Enemy and hazard design

Monsters and anomaly orbs exist, but spawning and behavior are generic. Hazards need per-planet spawn anchors, pacing rules, visible counterplay, and clearer mission-specific purpose.

Key files:
- `Space Game/Assets/Scripts/Hazards/RoamingMonster.cs`
- `Space Game/Assets/Scripts/Hazards/AnomalyOrb.cs`
- `Space Game/Assets/Scripts/Hazards/AnomalySpawner.cs`

Grooming questions:
- Which hazards belong to which planet/objective?
- Should hazards ramp over time?
- What should players learn visually/audio-wise before danger hits?

Recommendation: introduce a **`PlanetHazardSet.asset`** ScriptableObject referenced from `PlanetDefinition` declaring spawn rates, eligible hazards, and a ramp curve. Keeps hazard tuning data-driven (matches the SO-first rule). Per-planet flavor unlocks naturally — e.g. "Wraith Halo has anomaly orbs but no monsters; Cobalt Trench has slow monsters but flooding."

## 10. Dead-player loop

Dead-player carrying is implemented for body recovery, but the surrounding loop is not defined. Decide whether recovered bodies enable revive, count for extraction, provide score, or only avoid leaving players stranded.

Key files:
- `Space Game/Assets/Scripts/Player/NetworkFirstPersonController.PlayerCarrying.cs`
- `Space Game/Assets/Scripts/Player/PlayerInteractor.cs`

Grooming questions:
- Can dead players be revived?
- Does carrying a body to ship/launchpad matter?
- What does a dead player do while waiting?

Options:
- **A. Permadeath per round.** Bodies are cosmetic; dead players spectate until the next round.
- **B. Revive at the ship.** Carry the body to a medbay station; costs money/consumable to respawn.
- **C. Revive on extraction.** All carried bodies revive automatically when the crew extracts successfully. Bodies left on the planet stay dead.
- **D. Revive via launchpad.** Bodies dropped on the launchpad before takeoff get the player back.

Recommendation: **C (revive on extraction)** plus **partial spectator interaction** (can ping locations, can voice chat with the living). Makes the carry mechanic load-bearing without adding a new station, rewards crews that retrieve fallen friends, and gives the dead player a meaningful goal (help the living finish so I revive).

## 11. Runtime UI polish

The UI is generated in code. That is workable for now, but layout regressions are easy and richer screens will be clunky. Consider moving larger menus/panels to prefabs or a UI document workflow.

Key files:
- `Space Game/Assets/Scripts/UI/FriendSlopUI*.cs`

Grooming questions:
- Which UI should remain runtime-generated?
- Should planet selection, station screens, and inventory get dedicated prefabs?
- What minimum viewport sizes should be supported?

Options:
- **A. Stay procedural.** Lowest churn; each new screen adds a partial.
- **B. Move large screens to prefabs.** Menu, planet selection, station dialogs become prefabs assigned via `SerializeField`. HUD stays procedural.
- **C. UI Toolkit (UXML/USS).** Modern Unity UI; designer-friendly with hot reload; steeper authoring learning curve; mostly orthogonal to existing UGUI code.
- **D. Hybrid: prefabs + drive from `OnValueChanged` events.** Replace per-frame `RefreshUi` polling with subscriptions; move composite screens to prefabs.

Recommendation: **D**. The polling cost is the real pain point. Move large composite screens to prefabs as you add them; subscribe to `NetworkVariable.OnValueChanged` for per-widget updates; keep simple HUD widgets procedural for now. (First polling slice already shipped 2026-05-08.)

## 12. Lobby and matchmaking UX

Relay and LAN flows exist, but the experience is still code-entry driven. Public lobby browse, quick join, host settings, readiness, and player list controls are not fully built.

Key files:
- `Space Game/Assets/Scripts/Networking/NetworkSessionManager.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Menu.cs`

Grooming questions:
- Do players join by invite code only or browse public lobbies?
- Should the host configure seed, crew size, difficulty, and privacy?
- Should players ready up before round start?

Recommendation: stay code-based until the game is fun enough to invite strangers. When that day comes, **public lobby browser** > friend list (no platform dependency). Host config at lobby creation: privacy, max crew size. Skip ready-up; host starts the round. Mid-join allowed in `Lobby` and `Transitioning` phases only.

## 13. Chat

Chat is functional but minimal. It needs rate limiting, scrollback, mute/block options, system messages, and clearer lifecycle behavior before relying on it for public play.

Key files:
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Chat.cs`
- `Space Game/Assets/Scripts/Player/NetworkFirstPersonController.Chat.cs`

Grooming questions:
- Should chat persist through rounds?
- Should system events use the same chat feed?
- How should abuse/spam be handled?

Recommendation: defer chat polish until lobby/matchmaking decisions land — those will dictate whether chat needs heavy moderation tooling. For friend-only play (current state), keep chat simple: persist within session, add system messages for big events (deposits, deaths, extraction), skip moderation entirely. Voice chat: defer (Steam/Discord covers it for friend play).

## 14. Scene validation tooling

Add editor validation for planet scenes and catalog content. The validator should catch missing launchpads, missing return teleporters, missing spawn points, unreachable quotas, absent build-settings scenes, duplicate active environments, and stale prototype content.

Key files:
- `Space Game/Assets/Scripts/Editor/*`
- `Space Game/Assets/Scenes/*.unity`
- `Space Game/Assets/Planets/*.asset`

Grooming questions:
- Should validation run from a menu item, CI, or both?
- Which warnings should block a merge?
- Should each planet asset declare its expected minimum content?

## 15. Architecture follow-ups

Tracked engineering work surfaced in the 2026-05-03 audit. Each is its own focused PR — they intentionally were not bundled into [#18](https://github.com/TheSchlote/Friend-Slop/pull/18) so that scope and review stay tight.

Done and removed from this list: file-size splits (`NetworkLootItem`, `PlayerInteractor`, `FriendSlopUI.BuildUi`, `NetworkFirstPersonController`, `NetworkSessionManager`, `RoundManager`) all under 400 lines as of 2026-05-08; `RoundManager.Instance` and `NetworkFirstPersonController.LocalPlayer` migration to registries (see [architecture.md](docs/architecture.md) §D-014).

### 15c. Asmdef split — D-006 — **Done 2026-05-19**

Per [docs/architecture.md](docs/architecture.md) decision D-006: `FriendSlop.Runtime` was split into the layered graph **`Core ← { Networking, SceneManagement } ← Gameplay ← UI ← Editor`**. `FriendSlop.Runtime` renamed to `FriendSlop.Gameplay` (preserving the asmdef `.meta` GUID so any GUID-form refs survive). Two infra leaves above Core — `FriendSlop.Networking` (NGO session, Relay/Lobby/Auth, transport — the Steam swap surface) and `FriendSlop.SceneManagement` (NGO additive-scene transition service) — independently clean with no mutual edge. `PrototypeNetworkBootstrapper.{cs,Spawning.cs}` relocated to `Scripts/Session/` (Gameplay asmdef) because it's the gameplay composition root; keeping it in Networking would have created a wrong-direction edge. `ArchitectureGuardrailTests.AsmdefReferencesEnforceLayeredDirection` enforces the full graph at CI. Headless-validated (EditMode 147/147, PlayMode green); see D-006 in [architecture.md](docs/architecture.md) for the full rationale including the Steam-readiness contract.

### 15e. `FriendSlopUI` polling rewrite (remaining widgets)

First slice shipped 2026-05-08: full menu/layout refreshes are dirty/timed and driven by round `NetworkVariable` changes; loading progress and live HUD widgets stay on a lightweight per-frame path. Remaining work: convert individual HUD widgets (health/stamina) to direct player-state events instead of per-frame reads. Pairs with the prefab move tracked in §11.

### 15f. Test-coverage backfill

Currently no EditMode coverage for: `NetworkSessionManager` (mock-needed), `FriendSlopUI` partials, `PlayerInteractor`, `NetworkFirstPersonController` health/lifecycle/movement, weapons (`LaserGun`, `BoxingGloves`). The seams that already break are well-covered; gameplay surface that *could* break invisibly is not.

Lowest-hanging fruit: `LaserGun` / `BoxingGloves` — they're small, server-authoritative, and have clear pass/fail predicates.

## 16. Interiors merge follow-ups (queued on `tests/interior-coverage`)

Surfaced during the post-merge architectural review in [PR #24](https://github.com/TheSchlote/Friend-Slop/pull/24). All work is queued on the `tests/interior-coverage` branch.

### 16a. Documentation gaps — friends' agents will get bitten

The whole Interiors system was invisible to `CLAUDE.md` and `docs/architecture.md`, the NGO additive-scene spawn contract wasn't codified, and `Tools/Friend Slop/Repair Scene Wiring` wasn't listed next to the other editor menus.

**Status 2026-05-13: landed.** All 16a items shipped as a single doc PR on this branch.

- **`Space Game/CLAUDE.md`** — Unity bumped to `6000.3.15f1`; new hard rule 9 covers the NGO active-scene-before-Spawn contract and the `IsSpawned` filter; `Tools/Repair Scene Wiring` added under rule 2; Interiors subsection added to Architecture; `FriendSlop.Interiors` + `FriendSlop.Interiors.Blueprints` added to the namespace map; rule 5 asmdef wording updated to current state (Core/Foundation, Runtime, UI, Editor).
- **`docs/architecture.md`** — D-005 / D-006 / D-007 status updates and added decisions: **D-009** (interior generation as data-driven content), **D-010** (blueprints as authored interior layouts), **D-011** (NGO scene placement contract). Anti-patterns section gained `MoveGameObjectToScene`-after-Spawn, `destroyWithScene:false` defaulting, and unfiltered `FindObjectsByType` over NGO types.
- **`docs/FeatureIntegrationContracts.md`** — 4 new contracts: "New Networked Spawn," "New Building Type," "New Furniture Definition," "New Blueprint." PR checklist gained `ActiveSceneScope` + `IsSpawned` lines.
- **New `docs/InteriorSystem.md`** — full pipeline overview, procedural vs. blueprint path, networked-vs-local table, known oversized files.
- **New `docs/NetworkObjectSceneOwnership.md`** — codifies the active-scene-before-Spawn pattern, the `IsSpawned` filter, despawn-before-unload, and the known stale sites (`MeteorShower`, `AnomalySpawner`) queued in 16b.

### 16b. Code issues surfaced during the review — **Done 2026-05-15 (PR #33)**

All four items landed in [PR #33](https://github.com/TheSchlote/Friend-Slop/pull/33):

- `PlanetLootSpawner.IsCurrentActivePlanet()` now returns `false` when no `PlanetEnvironment` is in the hierarchy (was `true`).
- `MeteorShower.TrySpawnMeteor` swapped to the active-scene-swap-around-`Instantiate`+`Spawn(destroyWithScene:true)` pattern (matches `PlanetLootSpawner.TrySpawnNow`).
- `AnomalySpawner.TrySpawnOrb` resolves the active planet scene, swaps active scene, and spawns with `destroyWithScene:true` — anomalies tear down on planet travel instead of leaking into `DontDestroyOnLoad`.
- Dead test helper `AssertConnectedMenuLayoutDoesNotOverlap` (plus its orphaned `FindActiveRect`/`GetWorldRect` helpers) removed from `FriendSlopPrototypeSmokeTests`.

### 16c. Tests to add (priority order)

1. **`BlueprintLayoutBuilderTests`** (EditMode) — **Done 2026-05-19.** 16 EditMode tests covering empty/null inputs, placement + grid bookkeeping, floor-extent derivation, socket-adjacency connections, edge-state overrides (Wall/Open/Door + default), variant picking (across seeds + fallback), per-slot furniture-count override (with clone), and exit-room selection. Headless-validated EditMode 163/163. Pinned one current asymmetry: `Build(null, ...)` returns `FloorCount=0` (int default) while `Build(emptyBlueprint, ...)` returns `FloorCount=1` — callers must tolerate 0 from the null path.
2. **`BuildingDefinitionRoomPoolTests`** (EditMode) — defensive against future `FormerlySerializedAs` renames. The recent `roomPool` → `optionalPool` rename silently broke `InteriorLayoutGeneratorTests`. A direct unit test on `BuildingDefinition.RoomPool` (combines `optionalPool` + `requiredRooms.Definition`) would catch the next one before it bites.
3. **`PlanetLootSpawnerSceneOwnershipTests`** (PlayMode) — verifies the `ActiveSceneScope` fix on the Tier 2+ scene-owned spawner path that's not currently exercised. Load a Tier 2 scene, start host, assert all spawned loot ends up in the planet scene.
4. **`FurnitureSelectionTests`** (EditMode) — `HasTagOverlap`, `IsCappedOut`, `PickFurnitureForAnchor`, `OverlapsExisting` in `InteriorSceneBootstrapper.Furniture.cs`. Pure static helpers; easy to test.
5. **`InteriorSceneBootstrapper` PlayMode smoke** — spawn an interior, teleport a player in, assert rooms + doors present. Currently no coverage.
6. **`InteriorCatalogTests`** (EditMode) — catalog enumeration / lookup; ensure no null entries ship.

### 16d. Baselined oversized files (split before adding to)

Listed in `ArchitectureGuardrailTests.ExistingOversizedRuntimeFiles`. Split order recommended:

- `Assets/Scripts/Interiors/Blueprints/BlueprintEditorController.cs` — 545 lines, editor-only, smallest, safest first split.
- `Assets/Scripts/Interiors/InteriorSceneBootstrapper.cs` — 832 lines; natural extraction: wall-patching / garage-door geometry, then materials, then door spawning.
- `Assets/Scripts/Interiors/InteriorSceneBootstrapper.Furniture.cs` — 527 lines; extract pickers (`PickFurnitureForAnchor`, `HasTagOverlap`, etc.) into `.Furniture.Picking.cs`.
- `Assets/Scripts/Interiors/Blueprints/BlueprintEditorUI.cs` — 1005 lines; per-panel split.
- `Assets/Scripts/Interiors/InteriorLayoutGenerator.cs` — 1727 lines; biggest. Natural seams: required-room quotas, frontier expansion, downward-connector mirroring, fallback generation.

After each split: drop the file's entry from `ExistingOversizedRuntimeFiles` if the main file lands under 400.

### 16e. Editor migrations the agent can't run

These need a human in the editor; queued for the next playtest pass.

- Run `Tools/Friend Slop/Repair Scene Wiring` once to delete the stale ship root from `FriendSlopPrototype.unity` (the wiring repair was updated to handle this on its next run). After running, re-enable `ValidateBootstrapDoesNotOwnShipInterior` in `PlanetSceneValidator.TryValidate`.

## 17. Vendor quarantine + branch reconciliation (architecture D-012/D-013)

**Status 2026-05-18: substantially complete.** §17a/b/c landed — unreferenced packs dropped, kept packs relocated to `Assets/ThirdParty/` and wrapped in `ThirdParty.*` asmdefs (headless-validated, EditMode 137/137). Only the optional destructive history purge §17d (separately gated) and friend-branch reconciliation §17e remain; neither blocks the numbered roadmap. Vendor packs already on `main` total ~12,200 files (`LowPolyInterior2` 9,006, `LowPolyInterior` 1,714, `Plugins/Microdetail` 874, `HIVEMIND` 471, `YughuesFreeRockMaterials` 162) vs ~360 for our own `Prefabs/`. Three friend branches re-import overlapping copies of these, producing 1M+ line diffs, 13–15-file mutual conflicts, a 978 MB `.git`, and the recurring LFS-cost firefight. Goal: make every contribution reviewable and mergeable again without throwing away the friend's real work.

Staged deliberately. **17a–17c change no history and are safe to do unilaterally. 17d rewrites history and is explicitly a coordinated, gated step. 17e depends on 17c.**

### 17a. Stop the bleeding (no history change — do first)

- Land the D-012/D-013 policy (this commit: `architecture.md`, `CLAUDE.md`, this section).
- Tell the friend the new flow before they cut another branch: short-lived branch off fresh `main`, code + `.asset` only, vendor packs are separate import-once PRs, rebase before review.
- Add the quarantine scaffold so the convention exists: `Assets/ThirdParty/.gitkeep` (+ `.meta`) and a one-line `Assets/ThirdParty/README.md` pointing at D-012. No pack moves yet.
- Audit `.gitattributes` for binary extensions the current packs use that aren't LFS-tracked (e.g. `.exr`, `.hdr`, `.tif`, `.bmp`); extend coverage if any are committed as raw text. (Do **not** retroactively migrate yet — that's 17d.)

**Status 2026-05-15:** Scaffold landed (`Assets/ThirdParty/` + `README.md` pointing at D-012/D-013, with valid Unity `.meta`s). `.gitattributes` audit done and **clean** — no committed binary extension on `main` *or* the friend branches falls outside the existing LFS list (png/jpg/jpeg/tga/psd/fbx/blend/wav/mp3/ogg/mp4/mov). Confirms the bloat is 100% re-imported text (`.meta`/`.prefab`/`.mat`/`.asset` YAML), so the fix is quarantine + import-once (D-012/D-013), not more LFS. No `.gitattributes` change needed. Remaining 17a item: communicate the new flow to the friend before the next branch is cut.

### 17b. Inventory & per-pack decision (no history change)

For each pack — `HIVEMIND`, `LowPolyInterior`, `LowPolyInterior2`, `Plugins/Microdetail`, `YughuesFreeRockMaterials`, `_Recovery`, and the friend-branch-only `LowPolyMegaBundle` — classify **keep / relocate / drop** by evidence, not assumption:

- Extract the pack's asset GUIDs (`*.meta` `guid:`), then grep `Assets/{Scenes,Prefabs,Resources,Scripts}` and our `.asset` files for those GUIDs. Zero references in shipped content ⇒ drop candidate.
- Near-certain drops to verify first: `_Recovery/` (2 files, `0.unity` — a Unity crash-recovery scene dump, never referenced) and `LowPolyMegaBundle` (friend-branch-only, superseded by the split LowPoly packs). Confirm with the GUID grep before deleting.
- Output: a table in this section — pack → file count → decision → referencing scenes/prefabs.

**Status 2026-05-15 — inventory done (GUID-reference analysis, all file types incl. ProjectSettings/EditorBuildSettings):**

| Pack | Files | GUIDs ref'd by shipped content | Decision | Referenced by |
|---|---|---|---|---|
| `LowPolyInterior2` | 9,006 | **0** | **DROP** | — (never wired) |
| `LowPolyInterior` | 1,714 | **0** | **DROP** | — (never wired) |
| `_Recovery` | 2 | **0** | **DROP** | — (Unity crash dump) |
| `HIVEMIND` | 471 | 9 | RELOCATE | `Resources/Effects/BloodVfxLibrary.asset` (+ `Scripts/Effects/BloodSplatter.cs` code dep on `RealisticBlood`) |
| `Plugins/Microdetail` | 874 | 2 | RELOCATE | `Scenes/Planet_HillsAndValleys.unity` |
| `YughuesFreeRockMaterials` | 162 | 2 | RELOCATE | `Materials/RockTriplanar.mat` |

`LowPolyMegaBundle` was friend-branch-only; those branches are deleted, so it never reached `main` — nothing to do.

### 17c. Relocate kept packs (working-tree move — history preserved)

For each **keep**/**relocate** pack, one PR per pack (or one tight "relocate vendor" PR):

- `git mv "Space Game/Assets/<Pack>" "Space Game/Assets/ThirdParty/<Pack>"`. Asset GUIDs are unchanged by a move, so scene/prefab references survive — only `.asmdef`/`.asmref` paths, `*.rsp`, and any hard-coded `AssetDatabase` path strings in editor scripts need fix-ups.
- Add `Assets/ThirdParty/<Pack>/ThirdParty.<Pack>.asmdef`. Delete the pack's demo/example/sample-scene folders in the same PR.
- `dotnet build` the three csproj + run `tools\Run-UnityTests.ps1 -TestPlatform All`. Repo size is unchanged here (history still holds the blobs) but the tree is policy-compliant and future branches stay clean.

**Status 2026-05-15:** The three **DROP** packs (`LowPolyInterior`, `LowPolyInterior2`, `_Recovery` — 10,722 files, provably zero references) were removed in a dedicated working-tree-delete PR (`chore/drop-unreferenced-vendor`), separate from relocate because deleting unreferenced art carries no compile risk while the relocate does. **Remaining 17c work (own PR):** relocate `HIVEMIND`, `Plugins/Microdetail`, `YughuesFreeRockMaterials` into `Assets/ThirdParty/`. Delicacy: `HIVEMIND` is a *code* dependency (`Scripts/Effects/BloodSplatter.cs` → `RealisticBlood`), `Microdetail` has 67 scripts in the special `Assets/Plugins/` folder — both need correct `ThirdParty.<Pack>.asmdef` setup + `FriendSlop.Runtime` asmdef reference, with a `dotnet build` compile gate. `Microdetail` has `Materials/Demo` + `Textures/Demo` to strip; none have demo *scenes*.

**Status 2026-05-15 (relocate landed via PR #30/#31; last-mile re-scoped on `chore/vendor-quarantine-finish`):** the three packs now live under `Assets/ThirdParty/`. The two remaining 17c items were investigated headlessly and re-scoped — both turned out editor-bound or premise-wrong:

- **Demo-strip premise corrected by GUID grep.** `Assets/Scenes/Planet_HillsAndValleys.unity` references **2 of 13** Demo assets: `Microdetail/Textures/Demo/Grass005_2K-PNG_Color.png` (guid `8d6b5e1d88f760d40a266351aa17a9cd`) and `.../Ground054_2K-PNG_Color.png` (guid `c7b7c30782c0a604b89fc7be4afd420f`) — the shipped planet's ground textures. A blanket `Materials/Demo` + `Textures/Demo` delete **breaks Planet_HillsAndValleys**. The other 11 Demo assets are unreferenced. **Re-scoped item: content fix, not a quarantine strip** — re-point the Hills & Valleys terrain at non-Demo Microdetail textures (or knowingly keep the dependency); only then is a Demo strip safe. Do not strip blindly.
- **`HIVEMIND` is NOT a code dependency (earlier note corrected).** `grep RealisticBlood|HIVEMIND` across `Assets/Scripts` finds only a *comment* at `Scripts/Effects/BloodSplatter.cs:7`; blood VFX is wired via prefab/asset GUID (`Resources/Effects/BloodVfxLibrary.asset`), not code. ⇒ **no `FriendSlop.Runtime` → vendor asmdef edge is needed for any pack** (Microdetail = scene-GUID dep only — `Scripts` grep for Microdetail is empty; Yughues = materials only).
- **Yughues asmdef: DONE this PR.** `Assets/ThirdParty/YughuesFreeRockMaterials/ThirdParty.YughuesFreeRockMaterials.asmdef` added — 0 scripts ⇒ inert, zero compile risk; `autoReferenced:false` sets the D-012 convention for the deferred packs.
- **HIVEMIND + Microdetail asmdefs: deferred, editor-paired (NOT headless-safe).** An asmdef references nothing by default; these packs need an exact external-assembly list only Unity can validate (our `dotnet build` never compiles vendor scripts; an asmdef error blocks *all* play mode, surfacing only at the next playtest).

**Editor-paired asmdef spec — apply + validate in one Unity session, then drop this bullet and the `architecture.md` "paused behind §17" banner:**

- `Assets/ThirdParty/HIVEMIND/ThirdParty.HIVEMIND.asmdef` — 7 runtime scripts, no Editor folder, namespace `RealisticBlood.*`. `autoReferenced:false`. `references`: `Unity.RenderPipelines.Universal.Runtime` + `Unity.RenderPipelines.Core.Runtime` (scripts `using UnityEngine.Rendering.Universal`). `UnityEngine.VFX` is a built-in module (auto). Scripts also `using UnityEngine.Rendering.HighDefinition` (HDRP): add `Unity.RenderPipelines.HighDefinition.Runtime` **only if** the HDRP package is installed; if not, that `using` must already be `#if`-guarded to compile today — verify in-editor and mirror it (do not add an HDRP ref to a URP-only project). `using UnityEditor;` in `PlayAnimation.cs` is unguarded but editor-compile-clean (identical to today's Assembly-CSharp behavior; only an actual player build would fail, and the project never builds players).
- `Assets/ThirdParty/Microdetail/ThirdParty.Microdetail.asmdef` (root, runtime) **plus nested editor asmdefs** at `Microdetail/Scripts/Editor/` and `Microdetail/Editor/` (`includePlatforms:["Editor"]`, each referencing the root runtime asmdef). 43 runtime + 24 editor scripts, all `namespace Microdetail`; runtime `UnityEditor` calls are `#if UNITY_EDITOR`-guarded and fully-qualified (clean for a runtime asmdef). Runtime `references` must include `Unity.Collections` + `Unity.Mathematics` (hard `using`s); terrain/rendering/profiling are built-in modules (auto). `autoReferenced:false` on all three.
- Known vendor quirk (do **not** edit vendor code — D-012): `Microdetail/Editor/Scripts/SetupWizard.cs` hardcodes `Assets/Plugins/Microdetail/Pipelines`, stale after relocation; fires only if a user runs the SetupWizard (editor-setup-only, low impact). Flag, don't fix.
- Validate: 0 Console compile errors → enter Play → playtest `Planet_HillsAndValleys` (Microdetail terrain renders) and a blood-VFX scene (HIVEMIND). Then remove the `architecture.md` roadmap banner and close §17c.

**Status 2026-05-18 — §17c CLOSED (done headlessly, no editor session needed).** The "deferred / editor-paired (NOT headless-safe)" note above was wrong: the asmdefs were authored and validated entirely headlessly via batchmode `Run-UnityTests.ps1`. Landed: `ThirdParty.HIVEMIND` (URP + Core refs; HDRP fully `#if`-guarded out so no HDRP ref; `autoReferenced:false`), `ThirdParty.Microdetail` root runtime (Mathematics / Collections / **Core.Runtime** — SRP-Core `UnityEngine.Rendering.ObjectPool<T>` was the single fix-up iteration), plus nested `ThirdParty.Microdetail.Editor` (`Scripts/Editor/`, + TerrainTools) and `ThirdParty.Microdetail.SetupWizard` (`Editor/`). EditMode 137/137, zero regression. No vendor code edited (D-012). The `architecture.md` "paused behind §17" banner has been dropped. The `SetupWizard.cs` hardcoded-`Assets/Plugins/Microdetail/Pipelines` path quirk remains a flagged, unfixed vendor quirk (editor-setup-only, low impact) — do not edit vendor code to fix it.

### 17d. Optional coordinated history purge (DESTRUCTIVE — gated, not unilateral)

Only to reclaim `.git`/LFS size, and only for packs classified **drop** in 17b. Hard gate, all must hold before running:

1. The friend has **no open feature branch** (coordinate a window).
2. A full mirror backup exists (`git clone --mirror` pushed somewhere off-box) and the change is announced.
3. Every collaborator will re-clone fresh afterward (rebasing existing local work onto the rewritten history is not supported).

Then `git filter-repo --path <droppedpath> --invert-paths` (plus the matching LFS objects) on a mirror, validate, force-push, everyone re-clones. This is the only step that rewrites shared history; never do it as a side effect of another task.

### 17e. Salvage the friend's real code onto fresh branches (depends on 17c)

Per D-013, recover the gameplay code from the divergent branches without the vendor noise. Smallest first to prove the pattern:

1. `procedural-world-generation-tier-4-test` — 36 own-code files / 3.8K lines (planet terrain, ice planet, blood VFX hookup, test worlds).
2. `interior-variety` — 62 files / 8.7K lines (subset of below).
3. `interiors-changes` — 73 files / 14.3K lines (superset; full Interiors + Blueprints).

For each: branch off fresh `main`, bring over only `Space Game/Assets/Scripts/**` and genuine `.asset` content (not vendor dirs — those are now satisfied by the relocated `Assets/ThirdParty/` packs), resolve against current `main` (Interiors already partly landed via PR #24, so expect overlap), rebase, PR. `merge/all-branches-to-main` (0 conflicts vs `main`, 29 ahead) is the reference for what is already integrated — diff against it to avoid re-doing landed work.

Key files / branches:
- `origin/merge/all-branches-to-main` — clean integration reference.
- `origin/procedural-world-generation-tier-4-test`, `origin/interior-variety`, `origin/interiors-changes` — salvage sources.
- Stale post-merge branches to verify-then-delete: `origin/Interiors` (was PR #23).

## Decision dependencies

Some open-design decisions block or enable others. Suggested order to unblock the most downstream work: **§3 → §6 → §1 (loot question) → §11 → §9 → §2**, then the rest.

```
[§3 Progression endpoint] ─┬─> [§4 Ship stations: pilot]
                            └─> [§11 UI: planet selection screen]

[§6 Teleporter flow] ───────> [§1 Carried loot across travel]

[§7 Loot economy] ──────────> [§8 Combat: ammo refills via vending]

[§9 Hazard pacing] ─────────> [§2 Tier 2 identity]  (hazard set per planet)

[§12 Lobby UX] ─────────────> [§13 Chat moderation needs]
```
