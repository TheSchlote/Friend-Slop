# Friend Slop Backlog

This is the current gameplay and implementation backlog, ordered by the work that most affects future feature development. Prefer grooming the top items before adding more planet or objective content.

## 1. Planet lifecycle and scene ownership

The game is still split between prototype-nested planet content and additively loaded planet scenes. Finish the transition so each playable planet owns its scene, environment, launchpad, teleporters, spawns, loot budget, hazards, and cleanup rules.

Key files:
- `Space Game/Assets/Scripts/Round/RoundManager.cs`
- `Space Game/Assets/Scripts/Networking/PrototypeNetworkBootstrapper.cs`
- `Space Game/Assets/Scripts/Round/PlanetEnvironment.cs`

Grooming questions:
- Should the bootstrap scene contain only the ship/lobby plus managers?
- Should any planets remain nested in `FriendSlopPrototype.unity`?
- What should happen to carried/deposited loot when traveling?

## 2. Tier 2 planet identity

Several tier 2 planets are presented as different destinations but currently share Rusty Moon's scene/runtime environment. Give each tier 2 planet either a real unique scene or make the UI explicit that they are mission variants on the same planet.

Key files:
- `Space Game/Assets/Planets/Tier2_*.asset`
- `Space Game/Assets/Scenes/Planet_RustyMoon.unity`

Grooming questions:
- Are Cobalt Trench, Volt Foundry, Wraith Halo, and Rusty Moon unique places or mission variants?
- Should each variant have unique lighting, loot, hazards, and objective copy?

## 3. Progression ending and loop rules

`PlanetCatalog.MaxTier` supports up to tier 10, but authored content currently reaches tier 3. Define the win state, run loop, replay behavior, and whether final-tier success ends the expedition or keeps cycling.

Key files:
- `Space Game/Assets/Scripts/Round/PlanetCatalog.cs`
- `Space Game/Assets/Scripts/Round/RoundManager.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Menu.cs`

Grooming questions:
- What is the actual end of a run?
- Should final-tier success show credits, return to lobby, or start a harder loop?
- How should failed/all-dead states affect planet progression?

## 4. Ship stations

Ship stations currently support claiming/releasing occupancy, but station roles do not open real flows yet. Pilot, holographic board, module slot, customization bench, and future utility stations need concrete behavior.

Key files:
- `Space Game/Assets/Scripts/Ship/ShipStation.cs`
- `Space Game/Assets/Scenes/FriendSlopPrototype.unity`

Grooming questions:
- Which station controls travel?
- Which station previews planet choices?
- What does customization change, and is it cosmetic or gameplay-affecting?

## 5. Objective UX

Objectives work mechanically, but player-facing feedback is thin and success copy still assumes rocket assembly. Make objective titles, instructions, progress, success, and failure text objective-specific.

Key files:
- `Space Game/Assets/Scripts/Round/Objectives/*.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Menu.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Hud.cs`

Grooming questions:
- What should the HUD say at round start, mid-objective, extraction-ready, success, and failure?
- Should objective state have dedicated UI instead of one compact HUD line?

## 6. Teleporter flow

Teleporters are automatic trigger pads. This works, but can feel surprising during launchpad/extraction gameplay. Decide whether teleport should be automatic, interact-to-use, station-controlled, or phase-gated.

Key files:
- `Space Game/Assets/Scripts/Round/TeleporterPad.cs`
- `Space Game/Assets/Scripts/Round/RoundManager.cs`

Grooming questions:
- Should players press a key to teleport?
- Should carrying loot, carrying bodies, or being in extraction state alter teleport rules?
- Should the ship-side teleporter be active during every objective?

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

## 10. Dead-player loop

Dead-player carrying is implemented for body recovery, but the surrounding loop is not defined. Decide whether recovered bodies enable revive, count for extraction, provide score, or only avoid leaving players stranded.

Key files:
- `Space Game/Assets/Scripts/Player/NetworkFirstPersonController.PlayerCarrying.cs`
- `Space Game/Assets/Scripts/Player/PlayerInteractor.cs`

Grooming questions:
- Can dead players be revived?
- Does carrying a body to ship/launchpad matter?
- What does a dead player do while waiting?

## 11. Runtime UI polish

The UI is generated in code. That is workable for now, but layout regressions are easy and richer screens will be clunky. Consider moving larger menus/panels to prefabs or a UI document workflow.

Key files:
- `Space Game/Assets/Scripts/UI/FriendSlopUI*.cs`

Grooming questions:
- Which UI should remain runtime-generated?
- Should planet selection, station screens, and inventory get dedicated prefabs?
- What minimum viewport sizes should be supported?

## 12. Lobby and matchmaking UX

Relay and LAN flows exist, but the experience is still code-entry driven. Public lobby browse, quick join, host settings, readiness, and player list controls are not fully built.

Key files:
- `Space Game/Assets/Scripts/Networking/NetworkSessionManager.cs`
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Menu.cs`

Grooming questions:
- Do players join by invite code only or browse public lobbies?
- Should the host configure seed, crew size, difficulty, and privacy?
- Should players ready up before round start?

## 13. Chat

Chat is functional but minimal. It needs rate limiting, scrollback, mute/block options, system messages, and clearer lifecycle behavior before relying on it for public play.

Key files:
- `Space Game/Assets/Scripts/UI/FriendSlopUI.Chat.cs`
- `Space Game/Assets/Scripts/Player/NetworkFirstPersonController.Chat.cs`

Grooming questions:
- Should chat persist through rounds?
- Should system events use the same chat feed?
- How should abuse/spam be handled?

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
