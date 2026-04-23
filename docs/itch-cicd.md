# itch.io CI/CD Setup

This repo includes a GitHub Actions workflow that builds the Unity prototype for Windows and deploys it to itch.io with butler.

Workflow file: `.github/workflows/build-and-deploy-itch.yml`

## What It Does

- Builds the Unity project in `Space Game` with Unity `6000.3.4f1`.
- Targets `StandaloneWindows64`.
- Uploads the build as a GitHub Actions artifact.
- Pushes the build to `theschlote/<game-slug>:windows` when `BUTLER_API_KEY` is configured.

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

## Current Scope

This pipeline only builds Windows. Add more jobs later for Linux, macOS, or WebGL after the prototype stabilizes.

## References

- GameCI Unity activation: https://game.ci/docs/github/activation/
- GameCI Unity builder: https://game.ci/docs/github/builder/
- itch.io butler install: https://itch.io/docs/butler/installing.html
- itch.io butler authentication: https://itch.io/docs/butler/login.html
- itch.io butler pushing builds: https://itch.io/docs/butler/pushing.html
