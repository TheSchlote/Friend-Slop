# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Friend Slop** is a cooperative multiplayer space game built in Unity 6 (6000.3.4f1). Players explore a spherical planet, collect loot to meet a quota, assemble a rocket from three ship parts (Cockpit, Wings, Engine), and escape together via the launchpad.

## Build & Run

This is a Unity project. Use the checked-in PowerShell helpers for local validation:

```powershell
.\tools\Run-UnityTests.ps1 -TestPlatform All
.\tools\Configure-UnityMerge.ps1
```

- **Engine**: Unity 6000.3.4f1
- **Open the project** in Unity Hub, then open the main scene: `Assets/Scenes/FriendSlopPrototype.unity`
- **Play**: Click Play in the Unity Editor
- **Multiplayer testing**:
  - Local: use "Local Host" button in-game, then join from another client via localhost
  - Online: uses Unity Relay; host generates a join code, others enter it to connect

## Architecture

### Game Loop

Phases are defined in `Scripts/Round/RoundPhase.cs`: `Lobby -> Active -> Success | Failed`.

`RoundManager` (NetworkBehaviour) is the central orchestrator. It owns phase transitions, the countdown timer, quota tracking, ship part assembly state, and player boarding count. It is spawned server-side by `PrototypeNetworkBootstrapper` when the session starts.

### Spherical World & Physics

`SphereWorld` (`Scripts/Core/`) defines the planet's center and radius. **All movement and physics must orient relative to the sphere surface**; gravity points inward toward `SphereWorld.Center`. The player controller, monsters, and physics objects all use this for orientation. Never assume world-up is Vector3.up.

### Networking

- `NetworkSessionManager` handles session creation/joining via Unity Relay + Lobbies (online) or direct IP (LAN). Falls back to localhost when Relay is unavailable.
- `PrototypeNetworkBootstrapper` runs on the server: spawns `RoundManager`, loot items, and monsters on session start; cleans them up on stop.
- All gameplay state flows through `ServerRpc` / `ClientRpc` on `NetworkBehaviour` subclasses. The server owns authoritative state; clients request changes via ServerRpc.

### Player

- `NetworkFirstPersonController` handles first-person camera, WASD movement aligned to spherical gravity, jump, and stamina-gated sprint.
- `PlayerInteractor` manages the interaction loop: raycast focus detection, E to pick up, Q/right-click to drop/throw. A player can carry one item at a time; carrying reduces move speed via `carrySpeedMultiplier`.

### Loot & Submission

- `NetworkLootItem` is the base for all collectibles. It tracks whether it is a generic loot item or a ship part (`ShipPartType`: None, Cockpit, Wings, Engine), and manages physics state when carried vs. dropped.
- `DepositZone` accepts generic loot and adds its value toward the quota.
- `LaunchpadZone` accepts ship parts (updating `RoundManager`'s assembly state) and counts players boarding for the launch condition.
- `RocketAssemblyDisplay` updates visuals as parts are submitted.

**Launch condition**: all three ship parts submitted AND all active players standing on the launchpad.

### UI

`FriendSlopUI` handles both menus (host/join/start/restart) and the HUD (timer, quota bar, stamina bar, carried item). Menus block gameplay input; Tab/Esc toggles the gameplay menu during active play.

## Namespace Map

| Namespace | Location |
|---|---|
| `FriendSlop.Core` | `Scripts/Core/` |
| `FriendSlop.Networking` | `Scripts/Networking/` |
| `FriendSlop.Player` | `Scripts/Player/` |
| `FriendSlop.Loot` | `Scripts/Loot/` |
| `FriendSlop.Round` | `Scripts/Round/` |
| `FriendSlop.Hazards` | `Scripts/Hazards/` |
| `FriendSlop.UI` | `Scripts/UI/` |
| `FriendSlop.Interaction` | `Scripts/` (interface) |

## Key Constraints

- **Server authority**: State changes (item pickup, quota updates, phase transitions) must go through ServerRpc methods, not be applied client-side directly.
- **Spherical gravity everywhere**: Any new movement, physics, or spawn logic needs to orient using `SphereWorld.Center`, not world-up.
- **NetworkBehaviour lifecycle**: Objects with `NetworkBehaviour` must be spawned/despawned through `NetworkObject.Spawn()` / `Despawn()` on the server, not `Instantiate`/`Destroy` directly.
