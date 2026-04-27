# Spaceship and Scene Management

## Current Vertical Slice

- The prototype still loads `Assets/Scenes/FriendSlopPrototype.unity` as the single playable scene.
- A generated dev ship interior now exists inside that scene so the lobby is walkable immediately.
- `RoundManager` treats `Lobby`, `Success`, `Failed`, and `AllDead` as ship phases, and `Active` as the planet phase.
- Ship stations are reusable networked interactables so pilot controls, holographic boards, module bays, customization benches, and minigames can grow as separate systems.

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
