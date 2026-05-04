# Spaceship and Scene Management

## Current Audit (2026-05-03)

- Build Settings currently enable five scenes: `Assets/Scenes/FriendSlopPrototype.unity`, `Assets/Scenes/ShipInterior.unity`, `Assets/Scenes/Planet_StarterJunk.unity`, `Assets/Scenes/Planet_RustyMoon.unity`, and `Assets/Scenes/Planet_VioletGiant.unity`.
- `Assets/SceneDefinitions/MainGameSceneCatalog.asset` registers the dedicated ship scene (`ShipInterior_Scene.asset`) plus the three dedicated planet scenes: `Planet_StarterJunk_Scene.asset`, `Planet_RustyMoon_Scene.asset`, and `Planet_VioletGiant_Scene.asset`.
- `Planet_StarterJunk.unity` contains a `PlanetEnvironment` for `Tier1_StarterJunk.asset` with 4 player spawn points, 14 loot spawn points, 1 monster spawn point, a launchpad reference, a content root, and a `SphereWorld` reference. `Tier1_StarterJunk.asset` has `planetScene` assigned to `Planet_StarterJunk_Scene.asset`.
- `Planet_RustyMoon.unity` contains a `PlanetEnvironment` for `Tier2_RustyMoon.asset` with 4 player spawn points, 8 loot spawn anchors exposed on both `PlanetEnvironment` and `PlanetLootSpawner`, 2 monster spawn anchors, a launchpad reference, a return teleporter, and a `SphereWorld` reference. `Tier2_RustyMoon.asset` has `planetScene` assigned to `Planet_RustyMoon_Scene.asset`.
- `Planet_VioletGiant.unity` now owns the former nested Tier 3 content: a `PlanetEnvironment` for `Tier3_VioletGiant.asset` with 4 player spawn points, 2 monster spawn anchors, a launchpad reference, a return teleporter, and a `SphereWorld` reference. `Tier3_VioletGiant.asset` has `planetScene` assigned to `Planet_VioletGiant_Scene.asset`.
- `ShipInterior.unity` owns the former bootstrap ship root: `Bigger-On-The-Inside Ship Interior`, its `ShipEnvironment`, ship spawn points, ship stations, flat gravity volume, and the ship-side teleporter targeting the active planet.
- `FriendSlopPrototype.unity` no longer contains a `PlanetEnvironment` or ship interior root; it owns bootstrap/runtime systems such as `NetworkManager`, `NetworkSessionManager`, `FriendSlopUI`, `NetworkSceneTransitionService`, and `PrototypeNetworkBootstrapper`.
- `PrototypeNetworkBootstrapper` in the bootstrap scene still carries the legacy loot-prefab list, but its player, ship, loot, and monster spawn arrays are serialized as empty/null scene references. Runtime ship placement discovers `ShipEnvironment` after `ShipInterior` loads, planet spawning prefers active `PlanetEnvironment` anchors for split scenes, and scene-owned `PlanetLootSpawner` instances own their own loot pools.
- Planet travel cleanup now despawns loot and monsters only from the active planet scene when the active `PlanetEnvironment` is known. If no environment is registered, the old global cleanup behavior remains as the legacy fallback.
- Runtime code resolves the active round through `RoundManagerRegistry.Current` and the local player through `LocalPlayerRegistry.Current`; the old `RoundManager.Instance` and `NetworkFirstPersonController.LocalPlayer` facades have been removed from runtime code.
- `PlanetDefinition` scene assignment status: populated for `Tier1_StarterJunk.asset`, `Tier2_RustyMoon.asset`, and `Tier3_VioletGiant.asset`; null for `Tier2_DeepHaul.asset` (`Cobalt Trench`), `Tier2_QuickStrike.asset` (`Volt Foundry`), and `Tier2_GhostShift.asset` (`Wraith Halo`) because those remain Tier 2 mission variants that resolve through the shared Rusty Moon scene owner.
- Immediate migration implication: all currently authored tier-owner planets and the current ship lobby are scene-owned. Remaining scene-split work is to keep hardening validation/orchestration, keep Tier 2 variant UX explicit, and decide when any variant needs unique scene content.

## Tier 2 Scene Decision (2026-05-02)

- `Tier2_DeepHaul.asset` (`Cobalt Trench`), `Tier2_QuickStrike.asset` (`Volt Foundry`), and `Tier2_GhostShift.asset` (`Wraith Halo`) are treated as mission variants on the shared `Planet_RustyMoon.unity` scene.
- Each variant must keep an explicit `RoundObjective` assigned because its gameplay identity comes from objective/timer/quota data, not unique scene content.
- `Tier2_RustyMoon.asset` remains the Tier 2 scene owner with `planetScene` assigned to `Planet_RustyMoon_Scene.asset`.
- If a future playtest needs unique Tier 2 visuals, convert one variant at a time by adding a new `Planet_*` scene and assigning that variant's `planetScene`. Until then, do not create placeholder duplicate Tier 2 scenes.

## Current Vertical Slice

- The prototype still boots through `Assets/Scenes/FriendSlopPrototype.unity`, but the ship lobby and authored planets now have dedicated additive scene assets.
- The generated dev ship interior now lives in `Assets/Scenes/ShipInterior.unity` and is loaded by the bootstrapper for the network session before the round manager is spawned.
- `RoundManager` treats `Lobby`, `Success`, `Failed`, and `AllDead` as ship phases, and `Active` as the planet phase.
- Ship stations are reusable networked interactables so pilot controls, holographic boards, module bays, customization benches, and minigames can grow as separate systems.
- The first scene-management foundation is in code under `Assets/Scripts/SceneManagement`: scene definitions, a scene catalog, path validation, and a server-only Netcode transition service.
- The split is still transitional: `FriendSlopPrototype.unity` remains the bootstrap scene and still carries legacy planet fallback data, but authored ship and planet content are now outside it.

## Target Scene Layout

- `Bootstrap`: build index 0. Owns `NetworkManager`, session/lobby services, UI, persistent audio, save data, and scene orchestration.
- `ShipInterior`: additive, loaded for the whole network session. Owns ship geometry, ship spawn points, stations, customization hooks, and between-planet gameplay.
- `Planet_*`: additive, loaded only while visiting that planet. Owns terrain, hazards, loot spawn managers, objectives, lighting, and planet-specific runtime managers.
- `Travel_*`: optional additive overlay for route hazards or pilot minigames. It should not own player objects; it should feed route outcomes back into ship/round state.

## Rules

- The server is the only authority that starts network scene loads and unloads.
- Use Netcode for GameObjects integrated scene management while `NetworkConfig.EnableSceneManagement` is enabled.
- Load scenes by full asset path, not build index or bare filename.
- Put all loadable scenes in Build Settings or a future addressable/asset-bundle catalog.
- Keep persistent systems out of planet scenes. Planets should be replaceable content modules.
- Use in-scene `NetworkObject`s for static placed ship objects such as doors, consoles, whiteboards, and teleport pads.
- Use dynamically spawned network prefabs for objects that can be created, destroyed, pooled, carried, purchased, or migrated.
- Keep ship and planet spawn sets separate; never infer location from the current scene name.
- Test pure state rules in EditMode and run PlayMode scene smoke tests for every generated scene contract.

## Next Implementation Step

- Keep tightening bootstrap responsibilities now that `ShipInterior` is additive: session/UI/orchestration stay in bootstrap, authored ship content stays in `ShipInterior`.
- Keep planet-scene validation green as more scene-owned content is added.
- Decide when a Tier 2 variant should graduate from shared `Planet_RustyMoon` ownership into a unique scene.
- Continue preserving bootstrapper legacy planet fallbacks until they have a dedicated removal pass.
