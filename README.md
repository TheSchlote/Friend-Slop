# Friend Slop Prototype

Unity prototype for a small co-op sphere-world salvage run.

## Project

- Unity version: `6000.3.4f1`
- Unity project folder: `Space Game`
- Main scene: `Assets/Scenes/FriendSlopPrototype.unity`
- CI/CD setup: `docs/itch-cicd.md`
- Build version: `0.1.x`, with GitHub Actions setting `x` to the workflow run number
- Debug info: press `F3` in game to show version/session details for playtest reports

## Test Loop

1. Open `Space Game` in Unity.
2. Open `Assets/Scenes/FriendSlopPrototype.unity`.
3. Press Play.
4. Use `Host LAN` for the first local-network test.
5. Friend enters the host machine LAN IP in the input field and clicks `Join LAN`.
6. Host clicks `Start Round` once everyone is connected.

`Host Online` and `Join Code` use Unity Relay/Lobby. If Unity Services is not linked or Relay is unavailable, the host path falls back to LAN hosting.

## Controls

- `WASD`: move
- Mouse: look
- `Shift`: sprint
- `Space`: jump
- `E`: pick up / interact
- `Q`: drop carried item
- Right mouse: throw carried item
- `Tab`: toggle menu
- `F3`: toggle debug info
- `Esc`: unlock mouse

## Current Goal

Run around the tiny planet, collect money junk, and bring the three ship parts to the launchpad:

- Cockpit Nosecone
- Bent Rocket Wings
- Coughing Engine

When all three parts are installed, the rocket is assembled. Flying to the next sphere world is intentionally left for the next prototype step.

## Playtest Builds

GitHub Actions runs EditMode tests, builds the Windows player, and deploys it to itch.io with butler. See `docs/itch-cicd.md` for the one-time GitHub secret and itch.io page setup.
