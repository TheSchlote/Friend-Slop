# itch.io CI/CD Setup

This repo includes a GitHub Actions workflow that builds the Unity prototype for Windows and deploys it to itch.io with butler.

Workflow file: `.github/workflows/build-and-deploy-itch.yml`

## What It Does

- Builds the Unity project in `Space Game` with Unity `6000.3.4f1`.
- Runs EditMode tests and a PlayMode scene smoke test on pull requests, merge groups, and `main`.
- Targets `StandaloneWindows64`.
- Stamps each CI build as `0.1.<GitHub run number>`, which appears in-game and on itch.io.
- Writes `PLAYTEST_NOTES.txt` into each build with the version, commit, workflow run, and recent commits.
- Uploads the build as a GitHub Actions artifact.
- Pushes the build to `theschlote/<game-slug>:windows` only for `main` pushes or manual workflow runs, and only when the `itch-production` environment is approved and `BUTLER_API_KEY` is configured there.

The default itch.io game slug is `friend-slop`, so the default target is:

```text
theschlote/friend-slop:windows
```

If the itch.io page uses a different slug, set a GitHub Actions repository variable named `ITCH_GAME` to that slug.

## One-Time Account Setup

1. Create an itch.io project page under `https://theschlote.itch.io/`.
2. Use the page slug as the GitHub repository variable `ITCH_GAME`, unless the slug is `friend-slop`.
3. Create a GitHub Actions environment named `itch-production`.
4. Add `BUTLER_API_KEY` as an environment secret on `itch-production`.
5. Limit `itch-production` deployments to the `main` branch and add required reviewers if you want manual approval before deploys.
6. Add Unity license secrets for GameCI at the repository level.

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

Use the repository `Secrets` tab for Unity licensing:

```text
UNITY_LICENSE
UNITY_SERIAL
UNITY_EMAIL
UNITY_PASSWORD
```

Use the repository `Variables` tab for non-secret config:

```text
ITCH_GAME
```

Use the protected deployment environment for the butler token:

```text
Settings > Environments > itch-production
```

Add this environment secret there:

```text
BUTLER_API_KEY
```

## Running A Playtest Build

After the account setup is done:

1. Open a pull request to run EditMode tests, the PlayMode smoke test, and the Windows build without deploying.
2. Merge to `main` to let the deploy job become eligible, or open the GitHub `Actions` tab and run the workflow manually.
3. Approve the `itch-production` environment if required.

If `BUTLER_API_KEY` is missing from `itch-production`, the workflow still builds and uploads a GitHub artifact, but it will skip the itch.io deploy step.

If the Unity license secrets are missing, the workflow stops before the Unity build and prints which secrets need to be added.

## Current Scope

This pipeline only builds Windows. Add more jobs later for Linux, macOS, or WebGL after the prototype stabilizes.

## References

- GameCI Unity activation: https://game.ci/docs/github/activation/
- GameCI Unity builder: https://game.ci/docs/github/builder/
- itch.io butler install: https://itch.io/docs/butler/installing.html
- itch.io butler authentication: https://itch.io/docs/butler/login.html
- itch.io butler pushing builds: https://itch.io/docs/butler/pushing.html
