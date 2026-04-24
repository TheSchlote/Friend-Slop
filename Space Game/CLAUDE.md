# CLAUDE.md

This file gives repository-specific guidance to coding agents working in this Unity project.

## Project Overview

**Friend Slop** is a co-op multiplayer Unity 6 game prototype. Players explore a spherical planet, collect loot to meet a quota, assemble a rocket from three ship parts, and escape together from the launchpad.

## Build And Run

There are no standalone CLI build scripts for local development.

- Engine: Unity `6000.3.4f1`
- Open the Unity project folder: `Space Game`
- Main scene: `Assets/Scenes/FriendSlopPrototype.unity`
- Local multiplayer:
  - LAN: use the in-game `Host LAN` and `Join LAN` buttons
  - Online: use `Host Online` and `Join Code` through Unity Relay/Lobby
- CI workflow: `.github/workflows/build-and-deploy-itch.yml`
  - Pull requests and merge groups run EditMode tests, the PlayMode smoke test, and the Windows build
  - itch.io deployment only runs from `main` or manual dispatches, through the protected `itch-production` environment

## Architecture

### Game Loop

Phases are defined in `Assets/Scripts/Round/RoundPhase.cs`: `Lobby -> Active -> Success | Failed`.

`RoundManager` is the central orchestrator. It owns phase transitions, the countdown timer, quota tracking, ship part assembly state, and player boarding count. It is spawned on the server by `PrototypeNetworkBootstrapper` when a session starts.

### Spherical World And Physics

`SphereWorld` in `Assets/Scripts/Core/` defines the planet center and radius. All movement and physics must orient relative to the sphere surface. Gravity points inward toward `SphereWorld.Center`; do not assume `Vector3.up` is world up for gameplay code.

### Networking

- `NetworkSessionManager` handles Relay/Lobby hosting, join-code entry, and LAN fallback
- `PrototypeNetworkBootstrapper` spawns the round manager, loot, and monsters when the server starts
- `NetworkBehaviour` state is server authoritative; clients must request changes through RPCs instead of mutating gameplay state directly

### Player

- `NetworkFirstPersonController` handles first-person movement, spherical alignment, jumping, sprinting, stamina, and local diagnostics
- `PlayerInteractor` handles focus detection, pickup/drop/throw input, and carried item positioning

### Loot And Submission

- `NetworkLootItem` is the base type for all collectible loot and ship parts
- `DepositZone` accepts money loot and adds its value to the quota
- `LaunchpadZone` accepts ship parts and tracks whether all players have boarded
- `RocketAssemblyDisplay` updates rocket visuals as parts are submitted

Launch condition: all three ship parts submitted and all active players standing on the launchpad.

### UI

`FriendSlopUI` builds the HUD and menu at runtime. Menus block gameplay input, `Tab` toggles the menu pin, `Esc` unlocks or re-locks the mouse, and `F3` toggles the debug panel.

## Namespace Map

| Namespace | Location |
|---|---|
| `FriendSlop.Core` | `Assets/Scripts/Core/` |
| `FriendSlop.Networking` | `Assets/Scripts/Networking/` |
| `FriendSlop.Player` | `Assets/Scripts/Player/` |
| `FriendSlop.Loot` | `Assets/Scripts/Loot/` |
| `FriendSlop.Round` | `Assets/Scripts/Round/` |
| `FriendSlop.Hazards` | `Assets/Scripts/Hazards/` |
| `FriendSlop.UI` | `Assets/Scripts/UI/` |
| `FriendSlop.Interaction` | `Assets/Scripts/Interaction/` |

## Key Constraints

- Server authority: quota changes, loot state, phase changes, and launch conditions must be validated on the server
- Spherical gravity everywhere: new movement, spawn, and physics code should orient relative to `SphereWorld.Center`
- Network lifecycle: networked objects must be spawned and despawned through `NetworkObject`
- CI protections: do not weaken PR checks or deploy gating without also updating the repository docs and ruleset guidance in the root docs
