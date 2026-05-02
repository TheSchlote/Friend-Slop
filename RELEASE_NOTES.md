# Friend Slop Retrieval Release Notes

## Current playtest

### Added

- Walkable ship lobby with placeholder stations and ship spawn points.
- Tiered planet progression after the starter rocket assembly objective.
- Additive planet scene loading and cleanup for split planet content.
- Host planet-choice rolling and travel to higher-tier destinations.
- Ship-to-planet and planet-to-ship teleporters.
- New prototype hazards and tools, including anomaly orbs, boxing gloves, and laser gun.
- In-game chat and expanded HUD/objective feedback.

### Fixed and hardened

- Server-side validation for loot pickup range, player/body carrying, weapon cooldowns, and deposit hold timing.
- Planet travel cleanup so stale planet loot, hazards, and text do not survive into the next planet.
- Launchpad and teleporter scene wiring for starter and tier 2 planet flow.
- Loading screen progress behavior and UI overflow issues from the first planet travel pass.
- CI scene validation for planet build-settings entries.

### Known prototype limits

- Some tier 2 destinations currently behave as mission variants that share Rusty Moon scene content.
- Authored progression currently reaches prototype tier 3; later tiers need real content and end-of-run rules.
- Ship stations are placeholders and need real pilot, board, customization, and utility flows.
- Runtime-generated UI works for the prototype but still needs broader layout hardening.
- Public matchmaking is join-code based; there is no lobby browser or dedicated server flow yet.
