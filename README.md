# Friend Slop Prototype

Unity prototype for a small co-op sphere-world salvage run.

## Project

- Unity version: `6000.3.4f1`
- Unity project folder: `Space Game`
- Main scene: `Assets/Scenes/FriendSlopPrototype.unity`

## Test Loop

1. Open `Space Game` in Unity.
2. Open `Assets/Scenes/FriendSlopPrototype.unity`.
3. Press Play.
4. Use `Host LAN` for the first local-network test.
5. Friend enters the host machine LAN IP in the input field and clicks `Join LAN`.
6. Host clicks `Start Round` once everyone is connected.

For internet play, use `Host Online`, share the join code, and have your friend enter that code and click `Join Code`. This uses Unity's current Multiplayer Services package with Relay-backed sessions, so the Unity project must be linked to Unity Gaming Services.

## Controls

- `WASD`: move
- Mouse: look
- `Shift`: sprint
- `Space`: jump
- `E`: pick up / interact
- `Q`: drop carried item
- Right mouse: throw carried item
- `Tab`: toggle menu
- `Esc`: unlock mouse

## Current Goal

Run around the tiny planet, collect money junk, and bring the three ship parts to the launchpad:

- Cockpit Nosecone
- Bent Rocket Wings
- Coughing Engine

When all three parts are installed, the rocket is assembled. Flying to the next sphere world is intentionally left for the next prototype step.
