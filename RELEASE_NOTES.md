# Friend Slop Retrieval Release Notes

## Current playtest

### Added

- New tier 3 destination: **Ice Planet** with skating-friction surface.
- New tier 4 destination: **Hills and Valleys** (procedural icosphere displaced by noise).
- **Test Mode** lobby panel: hosts can launch directly into any catalog planet (including the Flat Test World prefab/asset showcase) without going through tier progression.
- **Meteor showers** as a server-driven hazard, with optional player-targeted bias and a low-chance direct-hit roll.
- Per-planet additive scenes: each tier-2+ destination and the ship lobby now live in their own scene asset, so loot/hazards/decals do not leak between planets.
- Final-tier expedition completion that records a completed run and returns the session to the tier-1 ship lobby instead of replaying the final planet.
- Smash-and-grab tier 2 quota variant and hold-the-pad tier 3 survival variant.
- Day/night cycle that survives planet swaps via a scene-aware registry.
- **Extraction-ready banner**: a transient pulsing on-screen banner now fires when a planet's objective is met and the crew should head to the launchpad (quota met / rocket assembled / survival extraction window), per objective type.

### Fixed and hardened

- Server-side loot settle: rested loot is frozen kinematic so curved-surface tangential gravity cannot drift items off planets; collisions wake them again.
- Server pickup line-of-sight aims at the loot's nearest bounds point instead of its AABB center, so flat items resting on the surface are no longer silently rejected.
- Per-planet travel cleanup is scoped to the active planet scene when a `PlanetEnvironment` is registered.
- Day/night HUD lookups no longer per-frame search the scene; a registry survives planet scene unloads cleanly.
- Roaming monster visibility sampling scales with distance (4 close samples → 1 torso-only at long range), reducing worst-case per-frame linecasts.
- Late-joining clients receive the correct monster-dead state through a `NetworkVariable` instead of missed `ClientRpc`s.
- Architecture-guardrail tests baseline file sizes so previously-oversized runtime files cannot grow further; many have been carved below the 400-line cap.

### Known prototype limits

- Authored progression currently reaches prototype tier 4; later tiers and final-run rules still need real content.
- Some tier 2 destinations remain mission variants whose unique scene/environment identity is being groomed.
- The Flat Test World is a prefab/asset showcase sandbox, not a real planet (no spherical gravity).
- Ship stations are placeholders and need real pilot, board, customization, and utility flows.
- Runtime-generated UI works for the prototype but still needs broader layout hardening.
- Public matchmaking is join-code based; there is no lobby browser or dedicated server flow yet.
