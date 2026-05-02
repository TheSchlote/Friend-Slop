# itch.io CI/CD Setup

This repo includes a GitHub Actions workflow that builds the Unity prototype for Windows and deploys it to itch.io with butler.

Workflow file: `.github/workflows/build-and-deploy-itch.yml`

## What It Does

- Builds the Unity project in `Space Game` with Unity `6000.3.4f1`.
- Runs EditMode tests and a PlayMode scene smoke test before building.
- Targets `StandaloneWindows64`.
- Stamps each CI build as `0.1.<GitHub run number>`, which appears in-game and on itch.io.
- Writes `PLAYTEST_NOTES.txt` and `RELEASE_NOTES.md` into each build with the version, commit, workflow run, curated release notes, and recent commits.
- Uploads the build as a GitHub Actions artifact.
- Pushes the build to `theschlote/<game-slug>:windows` when `BUTLER_API_KEY` is configured.
- Adds deployment/version details to the GitHub Actions step summary.

The default itch.io game slug is `friend-slop`, so the default target is:

```text
theschlote/friend-slop:windows
```

If the itch.io page uses a different slug, set a GitHub Actions repository variable named `ITCH_GAME` to that slug.

## One-Time Account Setup

1. Create an itch.io project page under `https://theschlote.itch.io/`.
2. Use the page slug as the GitHub repository variable `ITCH_GAME`, unless the slug is `friend-slop`.
3. Get a butler API key and add it as a GitHub Actions repository secret named `BUTLER_API_KEY`.
4. Add Unity license secrets for GameCI.

For a Unity Personal license, add these GitHub Actions repository secrets:

```text
UNITY_LICENSE
UNITY_EMAIL
UNITY_PASSWORD
```

For a Unity Pro or Plus license, add these GitHub Actions repository secrets:

```text
UNITY_SERIAL
UNITY_EMAIL
UNITY_PASSWORD
```

## Where To Add GitHub Secrets And Variables

In GitHub, open the repo and go to:

```text
Settings > Secrets and variables > Actions
```

Use the `Secrets` tab for private values:

```text
BUTLER_API_KEY
UNITY_LICENSE
UNITY_SERIAL
UNITY_EMAIL
UNITY_PASSWORD
```

Use the `Variables` tab for non-secret config:

```text
ITCH_GAME
```

## Running A Playtest Build

After the account setup is done:

1. Push to `main`, or open the GitHub `Actions` tab.
2. Select `Build Windows and Deploy to itch.io`.
3. Click `Run workflow`.

If `BUTLER_API_KEY` is missing, the workflow still builds and uploads a GitHub artifact, but it will skip the itch.io deploy step.

If the Unity license secrets are missing, the workflow stops before the Unity build and prints which secrets need to be added.

## Release Notes And itch.io Page Updates

Curated player-facing notes live in `RELEASE_NOTES.md` at the repository root. The workflow packages those notes into every Windows build and includes them in the GitHub Actions summary.

Do not automatically rewrite the public itch.io page or post a devlog on every `main` push. Most pushes are integration commits, and auto-posted commit summaries would make noisy public updates. For now:

1. Keep `RELEASE_NOTES.md` current when preparing a playtest build.
2. Let CI deploy the Windows channel with the generated `0.1.<run number>` version.
3. Manually copy the curated notes into an itch.io devlog when the build is worth notifying players about.

If the project later needs fully automated public release posts, add a separate manually-triggered workflow input so devlog publishing is deliberate instead of tied to every successful `main` build.

## Current Scope

This pipeline only builds Windows. Add more jobs later for Linux, macOS, or WebGL after the prototype stabilizes.

## Known CI Warning Policy

- GameCI `unity-test-runner` and `unity-builder` v4 currently target Node 20 in their action metadata. This repo sets `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` so GitHub-hosted runners execute them on Node 24. Re-check GameCI releases periodically; when a Node 24-compatible GameCI release is available, upgrade the actions and remove the force flag.
- Unity 6 URP/Core can print ray tracing shader compilation warnings for bundled render-pipeline resources on non-ray-tracing CI targets. The project is URP and does not enable ray tracing, so these warnings are treated as non-blocking unless the build fails or the project intentionally starts using ray tracing/probe-volume features.

## Coverage Gaps To Add

- PlayMode coverage for traveling from starter planet to tier 2 and back through both teleporters.
- Server-authority tests around loot deposit holds, weapon cooldowns, and pickup range checks.
- UI layout tests for the connected menu, loading screen, HUD objective copy, and small viewports.
- Multiplayer manual QA with at least two clients through lobby, starter planet, tier 2 travel, extraction, host shutdown, and reconnect.

## References

- GameCI Unity activation: https://game.ci/docs/github/activation/
- GameCI Unity builder: https://game.ci/docs/github/builder/
- itch.io butler install: https://itch.io/docs/butler/installing.html
- itch.io butler authentication: https://itch.io/docs/butler/login.html
- itch.io butler pushing builds: https://itch.io/docs/butler/pushing.html
