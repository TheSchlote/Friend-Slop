# Friend Slop Retrieval

Unity prototype for a small co-op sphere-world salvage run. Players host or join a session, scavenge junk and rocket parts on a tiny planet, avoid roaming monsters, and assemble a busted rocket at the launchpad.

## Project Snapshot

- Unity project folder: `Space Game`
- Unity version: `6000.3.4f1`
- Local Unity install used for verification: `T:\Unity\6000.3.4f1`
- Main scene: `Space Game/Assets/Scenes/FriendSlopPrototype.unity`
- Render pipeline: Universal Render Pipeline
- Multiplayer stack: Netcode for GameObjects, Unity Transport, Unity Relay/Lobby, Unity Authentication
- Input/UI stack: Unity Input System and UGUI
- CI/CD: GitHub Actions builds Windows and can deploy to itch.io

## Current Game Loop

1. Host or join a lobby.
2. The host starts the round.
3. Players spawn on a small spherical planet.
4. Find salvage and bring it to deposit zones for team money.
5. Find the three rocket parts and bring them to the launchpad.
6. Once the rocket is assembled, all connected players board the launchpad to complete the run.

Required ship parts:

- `Cockpit Nosecone`
- `Bent Rocket Wings`
- `Coughing Engine`

Flying to the next planet is not implemented yet; reaching the assembled/boarded state currently ends the run in success.

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
- Spherical gravity, planet-relative movement, jumping, sprinting, and surface alignment.
- Networked loot pickup, carrying, dropping, charged throwing, and depositing.
- Networked player carrying, including dead-player/body carrying.
- Health, death, revive-on-round-start, spectating, and all-dead wipeout state.
- Roaming monsters with vision/proximity detection, chasing, investigating, attacks, knockback, and damage.
- Rocket assembly display, launchpad submission, and boarded-player tracking.
- Runtime day/night sun visuals, skybox color changes, sun glare, planet color randomization, and runtime tree spawning.
- HUD for team money, ship-part status, health, stamina, carry prompts, death/spectate state, and round result.
- EditMode tests for join-code handling, round-state helpers, and carry-sync throttling.
- PlayMode smoke test for scene startup, host shutdown/restart, canceling a pending join, cursor state, and duplicate runtime object protection.

## Controls

- `WASD`: move
- Mouse: look
- Left mouse: click back into gameplay when the menu is not blocking input
- `Shift`: sprint
- `Space`: jump
- `E`: pick up loot, interact, or grab another player/body
- `Q`: drop carried loot/player
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
dotnet build 'T:\Repos\Friend-Slop\Space Game\FriendSlop.Runtime.csproj' /p:GenerateMSBuildEditorConfigFile=false
dotnet build 'T:\Repos\Friend-Slop\Space Game\FriendSlop.EditModeTests.csproj' /p:GenerateMSBuildEditorConfigFile=false
dotnet build 'T:\Repos\Friend-Slop\Space Game\FriendSlop.PlayModeTests.csproj' /p:GenerateMSBuildEditorConfigFile=false
```

Unity batch tests:

```powershell
$unity = 'T:\Unity\6000.3.4f1\Editor\Unity.exe'
$project = 'T:\Repos\Friend-Slop\Space Game'

& $unity -batchmode -projectPath "$project" -runTests -testPlatform EditMode -testResults "$env:TEMP\friend-slop-editmode-results.xml" -logFile "$env:TEMP\friend-slop-editmode.log"
& $unity -batchmode -projectPath "$project" -runTests -testPlatform PlayMode -testResults "$env:TEMP\friend-slop-playmode-results.xml" -logFile "$env:TEMP\friend-slop-playmode.log"
```

Do not write Unity test results under `Space Game/Temp`; Unity treats that folder as disposable.

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

Setup details are in `docs/itch-cicd.md`.

## Multiplayer QA

Manual multiplayer checks are tracked in:

- `docs/MultiplayerQA.md`

Run that checklist before publishing a playtest build or starting major new multiplayer features.

## Troubleshooting Notes

- GitHub's Node 20 action deprecation annotation is handled by `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` in the workflow.
- Unity Cloud/native-symbol warnings during CI are non-blocking unless the project is intentionally using Cloud Diagnostics or symbol upload.
- Root-level Unity `.log` files are ignored and should be treated as disposable local diagnostics.

## Known Prototype Limits

- No next-planet travel yet after rocket assembly.
- No public matchmaking UI beyond Relay join codes.
- No dedicated server flow.
- Visuals and UI are still prototype-quality.
- Relay requires Unity Services/Auth configuration for online play; LAN is the fallback path for local testing.
