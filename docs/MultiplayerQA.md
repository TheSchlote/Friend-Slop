# Multiplayer QA Checklist

Run these before adding major new multiplayer features or publishing a playtest build.

## Setup

- Use two standalone Windows builds when possible; editor plus build is useful, but it does not reproduce process-exit behavior as cleanly.
- Run one instance as host and one as client on the same machine with LAN first, then repeat with Relay if Unity Services is configured.
- Keep one test pass focused on normal Leave Session flows and one pass focused on hard exits such as Alt+F4 or killing the process.
- Confirm the client can use the UI immediately after each host-exit case; the cursor should be visible and buttons should respond without pressing Esc first.

## Session Startup

- Host Online shows a readable Relay join code and Copy Code copies the raw code.
- Join Code accepts pasted codes with spaces or hyphens.
- Join LAN works with `127.0.0.1` on the host machine.
- Host/join buttons cannot be spammed while a connection is already starting.
- Cancel during a pending join returns to the menu with a visible cursor.
- Invalid Relay code shows a player-readable error and allows retry.
- Slow Relay/Auth requests show a cancelable connecting state instead of leaving the menu looking frozen.
- Relay/Auth timeout returns to the menu with a retryable player-facing error.

## Disconnects

- Client leaves from lobby and host remains interactive.
- Client leaves during an active round and host remains interactive.
- Host leaves from lobby and client returns to an interactive menu.
- Host leaves during an active round and client returns to an interactive menu.
- Host Alt+F4 or process kill does not strand clients with a locked or hidden cursor.
- A failed or timed-out join can be cancelled or retried without restarting the game.

## Round Restart

- Host can start a round, leave, and host again in the same app session.
- Host can restart after success, failure, and wipeout without duplicate RoundManager, loot, or monsters.
- Late join during loading either syncs into the round or times out cleanly.

## Input/UI

- Lobby/menu cursor is visible without pressing Esc after hosting or joining.
- Tab and Esc both toggle the gameplay menu during active play.
- Clicking back into active play locks the cursor only when UI is not blocking input.
- Leave Session always returns to an interactive menu.
- Copy Code changes to a clear copied state and the pasted value joins successfully from the second client.

## Build/CI Notes

- The GitHub workflow opts JavaScript actions into Node 24 with `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true`.
- Unity Cloud/native-symbol warnings are non-blocking unless Cloud Diagnostics or symbol upload is intentionally required for the playtest.
- Local Unity logs and test result files should be written outside the repo or under ignored paths; avoid committing generated `.log` files.
