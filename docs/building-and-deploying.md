---
title: Building & deploying
nav_order: 7
---

# Building & deploying

## Game data is not in the repo

The ~453 MB game archive (`content/tribesinstall.7z`) is **not committed** (it exceeds GitHub's
file limits and isn't ours to redistribute). The base image consumes it via a **BuildKit bind
mount** at build time, so it never becomes an image layer. You must make it available in the
build context:

- **Locally** — place `content/tribesinstall.7z` in the repo before building.
- **In CI** — provide it via a GitHub **Release asset** or a URL (see below).

Everything else a mod needs (`content/classic_v152.zip`, `content/Construction_v0.70a.exe`) **is**
committed.

## A note on the VC++ runtime licensing

The image needs Microsoft's Visual C++ 2022 runtime DLLs (`msvcp140`, `vcruntime140`, `concrt140`,
…). Rather than committing those binaries, the build **downloads Microsoft's official
redistributable** (`VCREDIST_URL`, default `https://aka.ms/vs/17/release/vc_redist.x86.exe`) and
unpacks it with `cabextract` (the redist is a WiX "Burn" bundle whose payload cabs hold the DLLs as
`*.dll_x86`; only the needed set is copied into the Wine `system32`). This keeps **Microsoft as the
distributor** and keeps Microsoft binaries out of this repository and its history.

These files are Microsoft "Distributable Code." Redistribution is governed by the Visual Studio
2022 license terms (the `REDIST.TXT` list), which broadly permit shipping them *as part of an
application* subject to conditions (add significant functionality, flow-down terms, keep notices,
indemnify Microsoft, don't open-source-contaminate them). Note two caveats for this project: the
terms are written around running on **Windows** (here they run under **Wine on Linux**, a gray
area), and you should review the current terms yourself. To pin a specific build, set
`VCREDIST_SHA256`. *This is not legal advice.*

## Local builds

Build the **base first**; the mod images derive from it.

```bash
# base
docker build -f Dockerfile -t tribes2-server:base .

# derived mod images (FROM base)
docker build -f mods/classic/Dockerfile      --build-arg BASE_IMAGE=tribes2-server:base -t tribes2-server:classic .
docker build -f mods/construction/Dockerfile --build-arg BASE_IMAGE=tribes2-server:base -t tribes2-server:construction .
```

Pass build args with `--build-arg`, e.g. `--build-arg WINE_BRANCH=staging`,
`--build-arg PATCH_SHA256=<hash>`. See [Configuration → build args](configuration.md#build-args-image-build-time).

Run it:

```bash
docker run -d --name t2 -e ROOT_PASSWORD='choose-a-strong-one' \
  -p 8080:8080 -p 8443:8443 -p 28000:28000/udp \
  -v t2-data:/data \
  tribes2-server:base
```

Open `http://localhost:8080`, log in as `root`, complete first-time setup.

## Docker Compose

[`docker-compose.yml`](https://github.com/GeekOfWires/tribes2-server/blob/main/docker-compose.yml) defines three services that share a common env
anchor: `t2-base` (default) and `t2-classic` / `t2-construction` (behind profiles).

```bash
cp .env.example .env          # set ROOT_PASSWORD at minimum
docker compose build t2-base                                    # build base FIRST

docker compose up -d t2-base                                    # standard server (panel :8080)
docker compose --profile classic      up -d --build t2-classic      # Classic        (panel :8081)
docker compose --profile construction up -d --build t2-construction # Construction    (panel :8082)
```

Each service persists `/data` to its own named volume. Host ports are overridable via `.env`
(`PANEL_HTTP_PORT`, `CLASSIC_HTTP_PORT`, `GAME_PORT`, …).

To add your own mod service, see
[Creating a custom mod image → Compose](custom-mod-image.md#step-4--wire-it-into-docker-compose-optional).

## GitHub Actions / GHCR

[`.github/workflows/build.yml`](https://github.com/GeekOfWires/tribes2-server/blob/main/.github/workflows/build.yml) builds and pushes to **GHCR** on
push to `main`, on `v*` tags, or manually (`workflow_dispatch`):

- Job **`base`** builds `Dockerfile` → `ghcr.io/<owner>/<repo>/base:{sha,latest}`.
- Job **`mods`** is a matrix (`classic`, `construction`) that builds each `FROM` the base it just
  pushed → `ghcr.io/<owner>/<repo>/<mod>:{sha,latest}`.

Both use GitHub Actions cache (`type=gha`) and free up runner disk first.

### Providing game data to CI
The `base` job resolves `content/tribesinstall.7z` in this order:
1. already present in the checkout, else
2. **Release asset** named `tribesinstall.7z` — set the repo **variable** `GAMEDATA_RELEASE_TAG`
   to the release tag, else
3. the **`GAMEDATA_URL`** secret (a direct download URL).

If none is available the build fails with a clear error.

### Repo variables / secrets
| Name | Kind | Purpose |
|------|------|---------|
| `GAMEDATA_RELEASE_TAG` | variable | Release tag holding `tribesinstall.7z`. |
| `GAMEDATA_URL` | secret | Fallback direct URL for the game archive. |
| `PATCH_URL` | variable | Override the QoL patch URL. |
| `PATCH_SHA256` | variable | Pin the patch checksum. |
| `WINE_BRANCH` | variable | Wine branch. |

### Adding your mod to CI
Add it to the matrix:

```yaml
    strategy:
      matrix:
        mod: [classic, construction, mymod]   # ← your mod folder under mods/
```

It will publish `ghcr.io/<owner>/<repo>/mymod:{sha,latest}`. See
[Creating a custom mod image](custom-mod-image.md).

## Restart policy

The container expects to be long-running; `restart: on-failure` (Compose) relaunches it. When
**root force-shuts-down the panel**, the container stops and the restart policy decides whether it
comes back — a clean way to recycle.

## See also
- [Configuration reference](configuration.md) · [Creating a custom mod image](custom-mod-image.md)
- Back to [docs index](README.md)
