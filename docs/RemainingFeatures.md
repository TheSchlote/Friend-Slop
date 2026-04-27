# Remaining Feature Backlog

This file tracks planned features that are not finished yet. Keep entries scoped enough that they can become issues, branches, or test plans.

## Current Foundation

- Planet catalog, tiered planet definitions, round objectives, and prototype planet UI exist.
- Item slots, loot pools, rarity materials, planet loot spawning, and planet surface snapping exist.
- Walkable ship lobby foundation exists inside the prototype scene with pilot/utility stations.
- Network scene management scaffolding exists for future split scenes, but the project still ships from the prototype scene.
- EditMode and PlayMode test runners are available through `tools/Run-UnityTests.ps1`.

## Scene And Flow

- Split the prototype into dedicated boot, ship interior, and planet scenes.
- Add build settings validation so scene catalog entries must point to included scenes.
- Finish networked scene transitions from ship to planet and planet back to ship.
- Add late-join behavior tests for lobby, ship travel, and active planet states.
- Add reset flow that reliably returns all players to the ship lobby.

## Ship Interior

- Replace dev geometry with modular ship rooms, doors, stations, and clear navigation lanes.
- Add interactable pilot station ownership and role handoff.
- Add ship whiteboard or holographic board with networked drawings.
- Add ship AI voice/text system.
- Add interior customization hooks for colors, toys, minigames, stations, and unlockable props.
- Add exterior customization hooks for boosters, fins, hull repair, weapons, drills, scanners, sensors, and lights.

## Travel Gameplay

- Build rail-style piloting where one player steers while others can move around the ship.
- Add planet path choices, including left/right route decisions and a random gamble planet route.
- Add travel hazards such as asteroids, black holes, other spacecraft, distress signals, and combat encounters.
- Add between-planet minigames that can be optional or required before arrival.
- Add tests for pilot station authority, route choice replication, and travel failure/recovery.

## Planet Content

- Convert planet definitions into real scenes or additive scene modules.
- Add authored spawn nodes for loot, hazards, creatures, objectives, and exits.
- Add day/night cycles per planet where needed.
- Add background planets or solar-system/galaxy scale dressing.
- Add friendly creatures and non-player mimics.
- Add horror planet content with flesh trees and an unstoppable tracking creature.
- Add anomalies such as reverse controls, teleport, speed, jump, size, light, gold value, monster attraction, and time-of-day changes.

## Player Systems

- Expand character customization beyond humans into custom aliens.
- Add unlockable outfits, ranks, ship cosmetics, and interactables.
- Add stamina/helium supply behavior, including low-helium voice pitch, delayed refill, and pass-out states.
- Add health/organ UI concept and final death rules.
- Add forgiving fall damage tuned for slapstick player interaction.
- Decide blood/comedic gore limits and target age rating.

## Items, Economy, And Progression

- Add currency, shopping, and persistent unlock progression.
- Expand loot pools by planet tier, biome, rarity, and event.
- Add backpack/storage creature concept with weight or size limits.
- Add minified 3D UI for item slots and carried items.
- Add item value modifiers from anomalies and planet conditions.

## Enemies And Combat

- Decide whether weapons are player tools, ship systems, or both.
- Keep players intentionally weak even when combat exists.
- Add enemies that support death, escape, and comedic failure without making combat the main game.
- Add tests around death, spectating, item dropping, and round failure.

## Story And World

- Decide the core premise: crash survivors, ship graveyard scavengers, abandoned corporate workers, or another setup.
- Define why planets are small, why the ship is bigger inside, and why route choices change each run.
- Add at least one SS13 reference as an easter egg.
- Add lore delivery through ship AI, posters, logs, props, and planet discoveries.

## Engineering And QA

- Add automated tests for item slot replication, loot pool weighted selection, planet surface snapping, and ship station interaction.
- Add integration tests around scene catalog validation and scene transition state changes.
- Add multiplayer manual QA checklist for two to four players.
- Ensure CI runs EditMode tests, PlayMode tests, and a Windows build on `main`.
- Keep new systems modular through ScriptableObject definitions, small network components, and scene-owned composition.
