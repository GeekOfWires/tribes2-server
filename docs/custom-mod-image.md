---
title: Creating a custom mod image
nav_order: 6
---

# Creating a custom mod image

This is the end-to-end guide to building **your own server image with a custom mod and ruleset**,
deriving from `tribes2-server:base`. It mirrors how the bundled
[Classic](https://github.com/GeekOfWires/tribes2-server/blob/main/mods/classic/Dockerfile) and [Construction](https://github.com/GeekOfWires/tribes2-server/blob/main/mods/construction/Dockerfile) images
are built, then generalizes it.

> Just want to try a mod on a running server without building an image? Use the runtime upload
> path in [Rulesets & mods → B](rulesets-and-mods.md#b-upload-it-at-runtime-quick--experimental).

## Mental model

- The **base image** already contains the game, the QoL patch, Wine, and the panel. Its
  `GameData` has `base/` (core game) and `Tribes2.exe`.
- A **mod/ruleset** is a top-level folder under `GameData`, e.g. `GameData/MyMod/`, containing
  at minimum a **`scripts/`** dir (that's also how the panel *discovers* rulesets) and usually a
  **`prefs/`** dir for `serverprefs.cs`.
- Your derived image's job is to (1) **put your mod files in place** and (2) **set
  `SERVER_RULESET`** so the supervisor launches with `-mod <YourMod>`.

So every custom image is essentially:

```dockerfile
FROM tribes2-server:base
# ... copy/extract your mod into ${GAME_DIR}/MyMod ...
ENV SERVER_RULESET="MyMod"
```

`${GAME_DIR}` is already set in the base image to
`/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData`.

## Step 1 — lay out your mod files

Put your mod content under a folder named exactly as the ruleset, for example:

```
GameData/
  MyMod/
    scripts/        ← required (server-side .cs scripts; also makes it discoverable)
    prefs/
      serverprefs.cs
    ...other mod assets...
  base/
    MyMaps.vl2      ← maps go DIRECTLY in base/ (not in subfolders), see "Maps" below
```

`.cs` here is **TorqueScript**, not C#.

## Step 2 — write the Dockerfile

Create `mods/mymod/Dockerfile`. Pick whichever source pattern matches how your mod ships.

### Minimal template

```dockerfile
# syntax=docker/dockerfile:1.7
ARG BASE_IMAGE=tribes2-server:base
FROM ${BASE_IMAGE}

# 1. Put the mod files under GameData/MyMod
COPY mymod/ "${GAME_DIR}/MyMod/"

# 2. Make this image default to the MyMod ruleset (-> -mod MyMod)
ENV SERVER_RULESET="MyMod"

# 3. (optional but recommended) fail the build if the layout is wrong
RUN test -d "${GAME_DIR}/MyMod/scripts"
```

Build it (build the base first):

```bash
docker build -f Dockerfile -t tribes2-server:base .
docker build -f mods/mymod/Dockerfile --build-arg BASE_IMAGE=tribes2-server:base \
  -t tribes2-server:mymod .
```

### Variant: mod ships as a .zip

```dockerfile
COPY content/mymod.zip /tmp/mymod.zip
RUN mkdir -p "${GAME_DIR}/MyMod" \
 && 7z x -y /tmp/mymod.zip -o"${GAME_DIR}/MyMod" \
 && rm -f /tmp/mymod.zip \
 && test -d "${GAME_DIR}/MyMod/scripts"
ENV SERVER_RULESET="MyMod"
```

`7z` (`p7zip-full`) is already installed in the base image.

### Variant: mod is a git repo

`git` isn't in the base image, so install it for this layer and remove it again:

```dockerfile
ARG MYMOD_URL=https://github.com/you/MyMod.git
ARG MYMOD_REF=
RUN apt-get update && apt-get install -y --no-install-recommends git \
 && rm -rf /var/lib/apt/lists/* \
 && git clone --depth 1 "${MYMOD_URL}" /tmp/mymod \
 && if [ -n "${MYMOD_REF}" ]; then git -C /tmp/mymod fetch --depth 1 origin "${MYMOD_REF}" \
      && git -C /tmp/mymod checkout FETCH_HEAD; fi \
 && cp -rf /tmp/mymod/MyMod/. "${GAME_DIR}/MyMod/" \
 && rm -rf /tmp/mymod \
 && apt-get purge -y git && apt-get autoremove -y && rm -rf /var/lib/apt/lists/* \
 && test -d "${GAME_DIR}/MyMod/scripts"
ENV SERVER_RULESET="MyMod"
```

Pin `MYMOD_REF` to a tag/commit for reproducible builds.

### Variant: mod ships as a RAR self-extractor (.exe)

The base image's `p7zip-full` can't read RAR; install the full 7-Zip (`7zz`) for the layer:

```dockerfile
COPY content/MyMod_setup.exe /tmp/mymod.exe
RUN apt-get update && apt-get install -y --no-install-recommends 7zip \
 && rm -rf /var/lib/apt/lists/* \
 && 7zz x -y /tmp/mymod.exe -o"${GAME_DIR}" \
 && rm -f /tmp/mymod.exe \
 && apt-get purge -y 7zip && apt-get autoremove -y && rm -rf /var/lib/apt/lists/* \
 && test -d "${GAME_DIR}/MyMod/scripts"
ENV SERVER_RULESET="MyMod"
```

(This is exactly what the [Construction image](https://github.com/GeekOfWires/tribes2-server/blob/main/mods/construction/Dockerfile) does.)

### Adding maps

The engine only mounts **`.vl2` map packs placed directly in `GameData/base/`** — not in
subfolders. If your maps come in folders, flatten them:

```dockerfile
RUN find /path/to/maps -type f -name '*.vl2' -exec cp -f {} "${GAME_DIR}/base/" \; \
 && ls "${GAME_DIR}/base/"*.vl2 >/dev/null
```

### serverprefs.cs

You can bake a default `serverprefs.cs`:

```dockerfile
COPY mymod-serverprefs.cs "${GAME_DIR}/MyMod/prefs/serverprefs.cs"
```

…or leave it out and let root edit it in the panel during first-time setup (the panel creates
the `prefs/` dir automatically). See [Rulesets & mods → serverprefs](rulesets-and-mods.md#serverprefscs).

## Step 3 — verify the image

```bash
# files baked in + ruleset default set?
docker run --rm --entrypoint sh tribes2-server:mymod -c \
  'echo "SERVER_RULESET=[$SERVER_RULESET]"; ls -d "$GAME_DIR/MyMod/scripts"'

# run it and confirm the composed launch line + discovery
docker run -d --name t2mymod -e ROOT_PASSWORD=changeme -p 8080:8080 tribes2-server:mymod
# then, after logging in as root:
#   GET /api/server/status  -> "params": "-online -mod MyMod -dedicated", "ruleset": "MyMod"
#   GET /api/config/rulesets -> ["base","MyMod", ...]
```

The panel's ruleset combo will now suggest `MyMod` automatically (it discovers any `GameData`
folder containing `scripts/`).

## Step 4 — wire it into Docker Compose (optional)

Add a service mirroring the Classic/Construction ones in
[`docker-compose.yml`](https://github.com/GeekOfWires/tribes2-server/blob/main/docker-compose.yml):

```yaml
  t2-mymod:
    profiles: ["mymod"]
    build:
      context: .
      dockerfile: mods/mymod/Dockerfile
      args:
        BASE_IMAGE: tribes2-server:base
    image: tribes2-server:mymod
    container_name: tribes2-mymod
    restart: on-failure
    environment:
      <<: *common-env
      SERVER_RULESET: ${SERVER_RULESET:-MyMod}   # image default; override here if desired
    ports:
      - "${MYMOD_HTTP_PORT:-8083}:8080"
      - "${MYMOD_HTTPS_PORT:-8446}:8443"
      - "${MYMOD_GAME_PORT:-28003}:28000/udp"
    volumes:
      - mymod-data:/data
```

…and add `mymod-data:` under `volumes:`. Then:

```bash
docker compose build t2-base
docker compose --profile mymod build t2-mymod
docker compose --profile mymod up -d t2-mymod
```

## Step 5 — wire it into CI (optional)

The GitHub Actions workflow builds the base, then a matrix of mod images. To publish your image
to GHCR, add it to the matrix in [`.github/workflows/build.yml`](https://github.com/GeekOfWires/tribes2-server/blob/main/.github/workflows/build.yml)
(see [Building & deploying](building-and-deploying.md#github-actions--ghcr)).

## Gotchas

- **Case sensitivity.** Linux is case-sensitive; Windows/Wine is not. The folder name must match
  the ruleset name exactly (`MyMod` ≠ `mymod`). When overlaying archives that use a different
  case (the Classic zip ships lowercase `classic/`), **merge into** the correct-case folder
  rather than creating a second one.
- **Maps must be `.vl2` directly in `base/`.** Subfolders are not mounted.
- **`scripts/` makes it discoverable.** The panel lists a ruleset only if
  `GameData/<name>/scripts/` exists. (You can still type any name manually.)
- **Clean up build-time deps** (`git`, `7zip`) in the same `RUN` to keep layers small.
- **Don't override `LAUNCH_PARAMS` to add `-mod`.** Use `SERVER_RULESET` so the panel stays in
  control and `serverprefs`/discovery line up. (If you *do* hardcode `-mod` in `LAUNCH_PARAMS`,
  the supervisor won't double-insert it.)
- **The 453 MB game archive is not in the image** — the base image bind-mounts it at build time.
  Your derived image only adds your (small) mod layer.

## See also
- [Rulesets & mods](rulesets-and-mods.md) · [Building & deploying](building-and-deploying.md)
- Reference images: [mods/classic/Dockerfile](https://github.com/GeekOfWires/tribes2-server/blob/main/mods/classic/Dockerfile) ·
  [mods/construction/Dockerfile](https://github.com/GeekOfWires/tribes2-server/blob/main/mods/construction/Dockerfile)
- Back to [docs index](README.md)
