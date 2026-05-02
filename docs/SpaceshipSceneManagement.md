# Spaceship and Scene Management

## Current Audit (2026-05-02)

- Build Settings currently enable three scenes: `Assets/Scenes/FriendSlopPrototype.unity`, `Assets/Scenes/Planet_StarterJunk.unity`, and `Assets/Scenes/Planet_RustyMoon.unity`.
- `Assets/SceneDefinitions/MainGameSceneCatalog.asset` registers only the two dedicated planet scenes: `Planet_StarterJunk_Scene.asset` and `Planet_RustyMoon_Scene.asset`.
- `Planet_StarterJunk.unity` contains a `PlanetEnvironment` for `Tier1_StarterJunk.asset` with 4 player spawn points, 14 loot spawn points, 1 monster spawn point, a launchpad reference, a content root, and a `SphereWorld` reference. `Tier1_StarterJunk.asset` has `planetScene` assigned to `Planet_StarterJunk_Scene.asset`.
- `Planet_RustyMoon.unity` contains a `PlanetEnvironment` for `Tier2_RustyMoon.asset` with 4 player spawn points, a launchpad reference, and a `SphereWorld` reference, but no loot or monster spawn anchors yet. `Tier2_RustyMoon.asset` has `planetScene` assigned to `Planet_RustyMoon_Scene.asset`.
- `FriendSlopPrototype.unity` still contains nested planet content: a `PlanetEnvironment` named `Tier 3 Planet` for `Tier3_VioletGiant.asset`, with 4 player spawn points, a launchpad reference, and a `SphereWorld` reference. It has no loot or monster spawn anchors.
- `PrototypeNetworkBootstrapper` in the bootstrap scene still carries the legacy loot-prefab list, but its player, loot, and monster spawn arrays are serialized as null scene references. Runtime spawning therefore depends on the active `PlanetEnvironment` anchors for split scenes.
- `PlanetDefinition` scene assignment status: populated for `Tier1_StarterJunk.asset` and `Tier2_RustyMoon.asset`; null for `Tier2_DeepHaul.asset` (`Cobalt Trench`), `Tier2_QuickStrike.asset` (`Volt Foundry`), `Tier2_GhostShift.asset` (`Wraith Halo`), and `Tier3_VioletGiant.asset`.
- Immediate migration implication: Tier 1 is already largely split and should be verified/kept as the baseline. Remaining scene-split work is to finish anchors/content for `Planet_RustyMoon`, decide whether Tier 2 variants share that scene or become unique scenes, and extract the nested Tier 3 `Violet Giant` out of `FriendSlopPrototype`.

## Current Vertical Slice

- The prototype still boots through `Assets/Scenes/FriendSlopPrototype.unity`, but Tier 1 and one Tier 2 planet now have dedicated additive scene assets.
- A generated dev ship interior now exists inside that scene so the lobby is walkable immediately.
- `RoundManager` treats `Lobby`, `Success`, `Failed`, and `AllDead` as ship phases, and `Active` as the planet phase.
- Ship stations are reusable networked interactables so pilot controls, holographic boards, module bays, customization benches, and minigames can grow as separate systems.
- The first scene-management foundation is in code under `Assets/Scripts/SceneManagement`: scene definitions, a scene catalog, path validation, and a server-only Netcode transition service.
- The split is incomplete: `FriendSlopPrototype.unity` still owns ship/lobby systems and one nested Tier 3 planet.

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

- Finish separating `Bootstrap` and `ShipInterior` responsibilities inside or beyond `FriendSlopPrototype`.
- Complete per-planet anchor/content ownership for `Planet_RustyMoon`.
- Decide whether Tier 2 catalog entries are variants of `Planet_RustyMoon` or unique scenes.
- Move remaining nested planet content out of `FriendSlopPrototype` while keeping the same spawn/station contracts.
