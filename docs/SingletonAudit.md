# Singleton Audit

This audit covers project-owned singleton-style globals as of this branch. It does not include Unity/NGO service singletons such as `NetworkManager.Singleton` or Unity Services SDK accessors.

## Summary

| Global | Decision | Reason |
|---|---|---|
| `FriendSlopUI.Instance` | Removed | UI was only using itself as a lookup shortcut. Gameplay input blocking now goes through `GameplayInputState`, and UI session calls use a cached `NetworkSessionManager` reference. |
| `NetworkSessionManager.Instance` | Removed | The manager is a scene-owned component on the `NetworkManager` object. UI and tests can hold or discover that component directly without making it global state. |
| `NetworkSceneTransitionService.Instance` | Removed | The transition service is scene-owned infrastructure. `PrototypeNetworkBootstrapper` now passes it into the spawned `RoundManager`, which passes it to `PlanetSceneOrchestrator`. |
| `RoundManager.Instance` | Keep for now, migrate down | There is one authoritative networked round state owner per session, but the static is overused as a service locator across gameplay/UI. Removing it needs a staged migration by feature area. |
| `NetworkFirstPersonController.LocalPlayer` | Keep for now | This is a local-client player registry, not a game-wide manager. It is still global coupling, but it is defensible until player/UI presentation code is split further. |

## Current Rule

Do not add new project-owned `Instance` or `LocalPlayer` globals. `ArchitectureGuardrailTests.NoNewSingletonStyleGlobals` only allows:

- `RoundManager.Instance`
- `NetworkFirstPersonController.LocalPlayer`

Any change that adds another static object access point needs an explicit architecture review and a documented reason.

## Why These Three Were Removed

`FriendSlopUI.Instance`, `NetworkSessionManager.Instance`, and `NetworkSceneTransitionService.Instance` were not owning unique game state. They were convenience paths from one object to another:

- UI to session manager for host/join/cancel buttons and status text.
- Tests to scene components.
- Round scene orchestration to the scene-transition service.

Those are better represented as direct references, spawn-time configuration, or a small core service like `GameplayInputState`.

## Remaining Migration Plan

`RoundManager.Instance` should not be removed in one large sweep. It currently coordinates phase, objective state, loot deposit, launchpad boarding, player spawning, planet choice, and UI status. The safer path is to carve off read-only or narrow command surfaces:

- Phase readers should depend on a small phase source or subscribe to a round lifecycle event instead of taking the full manager.
- Zone components (`DepositZone`, `LaunchpadZone`, `TeleporterPad`, `ShipStation`) should move toward serialized references, spawn-time setup, or a round-context registration path.
- UI should eventually cache the active round from lifecycle events and stop polling `RoundManager.Instance` directly.
- New objectives must stay behind `RoundObjective` assets instead of adding more logic to `RoundManager`.

`NetworkFirstPersonController.LocalPlayer` can remain until the player/UI split creates a local-player provider. When that happens, prefer an evented provider (`LocalPlayerChanged`) over direct static polling.

## Best-Practice Guidance For Feature Agents

- Prefer `[SerializeField]` references for same-scene dependencies.
- Prefer spawn-time configuration for runtime-spawned objects.
- Prefer events or small context/provider components when many systems need the same state.
- Do not use a singleton to avoid wiring a reference.
- If a dependency is optional, resolve it once and cache it; do not search every frame.
