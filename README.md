# Friend Slop Retrieval

Unity prototype for a small co-op sphere-world salvage run. Players host or join a session, scavenge tiny planets, assemble a busted rocket, travel to higher-tier planets, and extract through launchpads and teleporters while avoiding prototype hazards.

## Project Snapshot

- Unity project folder: `Space Game`
- Unity version: `6000.3.4f1`
- Main scene: `Space Game/Assets/Scenes/FriendSlopPrototype.unity`
- Render pipeline: Universal Render Pipeline
- Multiplayer stack: Netcode for GameObjects, Unity Transport, Unity Relay/Lobby, Unity Authentication
- Input/UI stack: Unity Input System and UGUI
- CI/CD: GitHub Actions builds Windows and can deploy to itch.io

## Current Game Loop

1. Host or join a lobby.
2. Walk around the ship lobby while the host starts the round.
3. Spawn on the starter planet and collect salvage for team money.
4. Find the three rocket parts and bring them to the launchpad.
5. Once the rocket is assembled, all connected players board the launchpad.
6. The host chooses a rolled higher-tier destination.
7. Travel to the next planet, use ship/planet teleporters, complete that planet objective, and extract.

Required ship parts:

- `Cockpit Nosecone`
- `Bent Rocket Wings`
- `Coughing Engine`

Current authored content reaches prototype tier 3. Tier 2 contains several mission variants, with some variants intentionally sharing the same Rusty Moon scene while their final planet identities are still being groomed.

## Current Features

- Online hosting and joining through Unity Relay join codes.
- LAN hosting and joining for local testing.
- Readable host join-code panel with copy-to-clipboard support.
- Join-code normalization for pasted codes with spaces or hyphens.
- Cancelable pending joins, Relay/Auth service timeouts, and connection timeout handling.
- Connecting UI feedback with retry/cancel guidance for slow or failed network operations.
- Host/client leave handling that returns players to an interactive menu.
- Lobby queue display and player name entry.
- Restartable rounds without duplicating runtime managers, loot, or monsters.
- Walkable ship lobby with placeholder stations and ship spawn points.
- Additive planet scene loading/unloading for split planet content.
- Tiered planet catalog, host planet-choice rolling, and travel between planets.
- Ship-to-planet and planet-to-ship teleporter pads.
- Launchpad compass indicator and objective progress HUD.
- Spherical gravity, planet-relative movement, jumping, sprinting, and surface alignment.
- Networked loot pickup, carrying, dropping, charged throwing, and depositing.
- Networked player carrying, including dead-player/body carrying.
- Health, death, revive-on-round-start, spectating, and all-dead wipeout state.
- Roaming monsters with vision/proximity detection, chasing, investigating, attacks, knockback, and damage.
- Anomaly orbs, boxing gloves, and laser-gun prototype items.
- Rocket assembly display, launchpad submission, boarded-player tracking, and non-rocket objective support.
- In-game chat.
- Runtime day/night sun visuals, skybox color changes, sun glare, planet color randomization, and runtime tree spawning.
- HUD for team money, ship-part status, health, stamina, carry prompts, death/spectate state, and round result.
- EditMode tests for join-code handling, round-state helpers, carry-sync throttling, build-settings scene wiring, and planet scene launchpad/teleporter readiness.
- PlayMode smoke test for scene startup, additive starter planet load, host shutdown/restart, canceling a pending join, cursor state, and duplicate runtime object protection.

## Controls

- `WASD`: move
- Mouse: look
- Left mouse: click back into gameplay when the menu is not blocking input
- `Shift`: sprint
- `Space`: jump
- `E`: pick up loot, interact, or grab another player/body
- `Q`: drop carried loot/player
- `F`: hold to deposit carried loot while inside a deposit surface
- Left mouse: fire/use supported carried tools
- Hold right mouse, then release: charged throw for carried loot/player
- `Tab` or `Esc`: toggle gameplay menu during active play
- `E` / `Q` while dead and spectating: cycle spectate target
- `F9`: start/stop local physics diagnostics
- `F10`: write one local physics diagnostic sample

## Running Locally

1. Open `Space Game` in Unity `6000.3.4f1`.
2. Open `Assets/Scenes/FriendSlopPrototype.unity`.
3. Press Play.
4. Use `Host LAN` for quick single-machine or LAN testing.
5. Use `Host Online` to create a Relay session and display a copyable join code.
6. Use `Join Code` or `Join LAN` from another client.
7. Host clicks `Start Round` once everyone is ready.

If Relay services are unavailable, `Host Online` falls back to LAN hosting.

## Scene and Prefab Tools

The prototype scene and generated prefabs are maintained by editor tooling:

- `Tools/Friend Slop/Rebuild Prototype Scene`
- `Tools/Friend Slop/Repair Prototype Scene`

Use these from the Unity editor when generated scene references or network prefab lists need repair.

## Verification

Local C# compile checks:

```powershell
dotnet build '.\Space Game\FriendSlop.Runtime.csproj' /p:GenerateMSBuildEditorConfigFile=false
dotnet build '.\Space Game\FriendSlop.EditModeTests.csproj' /p:GenerateMSBuildEditorConfigFile=false
dotnet build '.\Space Game\FriendSlop.PlayModeTests.csproj' /p:GenerateMSBuildEditorConfigFile=false
```

Unity batch tests:

```powershell
.\tools\Run-UnityTests.ps1 -TestPlatform All
.\tools\Run-UnityTests.ps1 -TestPlatform EditMode
.\tools\Run-UnityTests.ps1 -TestPlatform PlayMode
```

The test script resolves Unity from `UNITY_EXE`, Unity Hub install paths, Unity Hub secondary install paths, then PATH as a fallback. If Unity is installed somewhere unusual, set a user-level override:

```powershell
[Environment]::SetEnvironmentVariable('UNITY_EXE', '<absolute-path-to-Unity.exe>', 'User')
```

The script writes test results and logs to `$env:TEMP\FriendSlopUnityTests` by default. Do not write Unity test results under `Space Game/Temp`; Unity treats that folder as disposable. This project's Unity Test Framework should not be run with `-quit`; the checked-in script intentionally omits it so TestRunner can exit Unity after the run.

Unity version drift check:

```powershell
.\tools\Assert-UnityVersion.ps1 -ExpectedUnityVersion 6000.3.4f1
```

Unity YAML merge setup for this checkout:

```powershell
.\tools\Configure-UnityMerge.ps1
```

Current automated coverage:

- EditMode: join-code utility, round-state utility, carry-sync utility.
- PlayMode: prototype scene smoke test covering host startup, shutdown, restart, pending join cancel, connection timeout UI state, cursor state, and runtime duplicate protection.

## CI/CD

GitHub Actions workflow: `.github/workflows/build-and-deploy-itch.yml`

The workflow:

- Runs EditMode tests.
- Runs PlayMode smoke tests.
- Builds a Windows player.
- Uploads the Windows artifact.
- Deploys to itch.io when configured for `main`.
- Packages `PLAYTEST_NOTES.txt` and `RELEASE_NOTES.md` into each build.

Setup details are in `docs/itch-cicd.md`.

## Release Notes

Curated player-facing notes live in `RELEASE_NOTES.md`. The CI build copies those notes into the downloadable Windows build and the GitHub Actions summary. The itch.io page and devlog should stay manually curated for now; automatic page/devlog edits on every `main` push would publish noisy commit-level notes instead of useful playtest notes.

## Multiplayer QA

Manual multiplayer checks are tracked in:

- `docs/MultiplayerQA.md`

Run that checklist before publishing a playtest build or starting major new multiplayer features.

## Troubleshooting Notes

- GameCI v4 currently emits GitHub's Node 20 action deprecation annotation. The workflow sets `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true`; remove it once GameCI publishes Node 24 action metadata.
- Unity 6 URP/Core may emit non-blocking ray tracing shader warnings during CI Windows builds even though this project does not use ray tracing. Treat those as package shader-variant noise unless the build fails or ray tracing/probe-volume features are intentionally added.
- Unity Cloud/native-symbol warnings during CI are non-blocking unless the project is intentionally using Cloud Diagnostics or symbol upload.
- Root-level Unity `.log` files are ignored and should be treated as disposable local diagnostics.

## Known Prototype Limits

- Current planet progression is prototype-depth only; authored content reaches tier 3, while the catalog supports more tiers.
- Some tier 2 destinations are mission variants that share Rusty Moon's scene/environment.
- Ship stations are placeholders; pilot, board, customization, and utility station flows need grooming before they become real gameplay.
- Runtime-generated UI is functional but still needs layout hardening and eventual prefab/UI-document treatment for larger screens.
- No public matchmaking UI beyond Relay join codes.
- No dedicated server flow.
- Visuals and UI are still prototype-quality.
- Relay requires Unity Services/Auth configuration for online play; LAN is the fallback path for local testing.

## Backlog

Open design, gameplay, and engineering grooming items are tracked in `BACKLOG.md`.
