# Tribes 2 Dedicated Server Container

A single Docker image that runs a **Tribes 2** (2002) dedicated server under **Wine** on
**Debian**, applies the **Tribes 2** community patch, and ships an integrated
**ASP.NET Core 10** control panel (React SPA frontend, ASP.NET Identity auth) with
role-based access. The panel is **PID 1** and owns the game lifecycle.

> **Engine note:** Tribes 2 runs on the Dynamix **"V12" engine** — the precursor to the
> Torque Game Engine (TGE), which preceded Torque Game Engine Advanced (TGEA), which preceded
> Torque 3D. The telnet remote console the panel relies on originates in this lineage.

## What the build does

1. `FROM debian:trixie-slim`, enables **i386** multiarch.
2. Installs **WineHQ** (`winehq-stable`) with 32-bit components.
3. Initializes a 32-bit Wine prefix and installs runtimes via winetricks:
   - `vcrun6` — MSVC 6.0-era runtime the original engine links against.
   - `vcrun2015` (UCRT) — modern runtime surface needed by the rebuilt 2 MB
     `IFC22.dll` (the Ruby-based Tribes 2 loader).
   - `msvcrt` set to `native,builtin` (Tribes 2 Ruby requirement).
4. Extracts `content/tribesinstall.7z` so `GameData` lands at
   `C:\Dynamix\Tribes2\GameData`.
5. Downloads the **Tribes 2 QoL patch** (`PATCH_URL` build ARG — the "variable";
   defaults to `TribesNEXT_20250922_preview.exe`). It's an **NSIS installer**, so `7z`
   extracts its payload deterministically (`IFC22.dll`, Miles sound libs, SDL3/OpenAL/
   Discord/libcurl, `base/t2csri.vl2`, …) straight onto `GameData` — **no Wine-run of the
   GUI installer required**.
6. Runs the Python PE patcher (`content/tribes_dual_patcher.py`) over `Tribes2.exe`:
   NOPs its single `AllocConsole` call-site and flips the PE subsystem GUI→CUI so the
   dedicated server attaches to the launcher's stdio **without needing an X/GUI console**.
7. Publishes the **ASP.NET Core 10 panel** (with the built React SPA) self-contained and
   sets it as the entrypoint (PID 1).

> The patcher only affects the dedicated-server console path — `Tribes2.exe` contains
> exactly one `AllocConsole` call (verified), so there is nothing else to touch.

## Runtime architecture (single container)

```
PID 1: ASP.NET Core panel (Kestrel + React SPA + ASP.NET Identity)
  └─ GameSupervisor (hosted Worker Service)
       ├─ wine Tribes2.exe $LAUNCH_PARAMS   (stdout -> ConsoleHub ring buffer + SSE)
       ├─ telnet client -> in-game V12-engine console (quit(); + arbitrary commands)
       └─ lifecycle: restart / force-restart / stop / start + internal auto-restart

browser --HTTPS+cookie (Identity)--> panel API/SSE --> GameSupervisor --> game
```

- **The panel owns the lifecycle.** The game runs inside the panel process; crashes are
  auto-restarted internally so the panel stays available. `restart: on-failure` only relaunches
  the *container* if the panel itself exits (e.g. root's force-shutdown).
- **Console feed** = captured game stdout, streamed to the browser over SSE.
- **Commands/quit** = the engine's telnet remote console, enabled by an injected `autoexec.cs`
  (`telnetSetParameters`; the stock `-telnetParams` handler has an empty-listen-pass bug).
- **Tech**: ASP.NET Core 10 · React (Vite) SPA served by the host · **ASP.NET Core Identity**
  (cookie auth) · **EF Core 10** on a **local Turso-compatible SQLite** file · LettuceEncrypt for ACME.

### Database

The panel DB is a single local file (`PANEL_DB_PATH`, default `/data/panel.db`) on a Docker
volume. EF Core 10 talks to it via the official SQLite provider, and the file is plain
SQLite-format — i.e. a **local Turso database**. You can inspect
or manage it locally with the Turso CLI:

```bash
turso db shell /data/panel.db        # also works with the sqlite3 or libsql CLIs
```

(There is no EF Core 10 provider for the new Turso/Limbo engine or for libSQL today, so EF Core's
SQLite provider on the Turso-compatible file is the robust local-only choice.)

## Roles

| Role         | Capabilities |
|--------------|--------------|
| User         | View live server console only |
| Admin        | + **Restart** (graceful `quit();`, auto-relaunch) / Start |
| Super Admin  | + Graceful **stop** + run console commands + **Force-restart** the game (emergency kill + relaunch) |
| root         | + **Force-shutdown the panel** (stops the container; restart policy relaunches it) + **user management** |

All privileged actions are recorded in an **audit log** (visible to Super Admin+).

## Quick start

```bash
cp .env.example .env
# Set at least PANEL_ROOT_USERNAME + PANEL_ROOT_PASSWORD (root is seeded on first boot).
# Optionally enable TLS (SELF_SIGNED_CERT=1 or LETS_ENCRYPT_CERT=1) — see .env.example.

docker compose build t2-base
docker compose up -d t2-base
docker compose logs -f                  # watch the panel + game console
# Panel: http://localhost:8080  (log in as your root user)
```

### Server variants / images

Three images are defined. The derived ones build `FROM` the base, so **build the base first**.

| Image | Mod | Builds from | LAUNCH_PARAMS |
|-------|-----|-------------|---------------|
| `tribes2-server:base` | base | [Dockerfile](Dockerfile) | `-online -dedicated` |
| `tribes2-server:classic` | Classic | [mods/classic/Dockerfile](mods/classic/Dockerfile) | `-online -mod Classic -dedicated` |
| `tribes2-server:construction` | Construction | [mods/construction/Dockerfile](mods/construction/Dockerfile) | `-online -mod Construction -dedicated` |

- **Classic** overlays `content/classic_v152.zip` (a zip-in-a-zip; its lowercase `classic/`
  is **merged into** the case-sensitive `Classic/`), then clones
  [TacoServer](https://github.com/ChocoTaco1/TacoServer) and overlays its `Classic/` tree, and
  finally clones [TacoMaps](https://github.com/ChocoTaco1/TacoMaps) and flattens its `.vl2`
  map packs into `base/` (the engine only mounts `.vl2` files placed directly in `base/`).
- **Construction** extracts `content/Construction_v0.70a.exe` (a RAR self-extractor, unpacked
  with `7zz`) so `Construction/` lands in `GameData`.

```bash
docker compose build t2-base                                   # build base FIRST
docker compose --profile classic      build t2-classic         # then the derived images
docker compose --profile construction build t2-construction

docker compose up -d t2-base                                   # standard server (panel :8080)
docker compose --profile classic      up -d t2-classic         # Classic        (panel :8081)
docker compose --profile construction up -d t2-construction    # Construction    (panel :8082)
```

`LAUNCH_PARAMS` ordering matters: `-online` (or `-nologin` to host outside WON/Tribes 2
auth) **first**, `-dedicated` **last**, mods in between. The mod images bake the correct
value; override the env only if needed.

## Verifying the patch (inside the built image)

```bash
docker run --rm --entrypoint bash tribes2-server:base -c '
  ls -l "$GAME_DIR/IFC22.dll";                       # ~2 MB (Tribes 2)
  python3 /opt/patcher/tribes_dual_patcher.py --exe "$GAME_DIR/Tribes2.exe" --dry-run'
# subsystem should read CUI and 0 AllocConsole call-sites remaining to patch.
```

## Game data (`tribesinstall.7z`)

The 453 MB game archive is **never committed to git** — GitHub rejects regular files >100 MB and
we don't use Git LFS. Instead the Dockerfile reads it from the **build context** at
`content/tribesinstall.7z`, and something puts it there depending on where you build:

- **Local builds:** keep the file on disk at `content/tribesinstall.7z`. `docker compose build`
  bind-mounts it during the build (it is *not* copied into an image layer). It's git-ignored, so
  it won't accidentally get committed.
- **GitHub CI:** upload the same file **once** as a GitHub **Release asset** (Release assets allow
  up to 2 GB, are free, and don't count against any LFS quota). The workflow downloads it into the
  context before building.

One-time CI setup (uses the built-in token, no secret needed):

```bash
# create a release that holds the game data and attach the 7z
gh release create gamedata-v1 content/tribesinstall.7z --title "Game data" --notes "tribesinstall.7z"
# tell the workflow which release to pull from:
gh variable set GAMEDATA_RELEASE_TAG --body gamedata-v1
```

(Helper: `scripts/publish-gamedata.sh gamedata-v1` does both.) The workflow's *Provide game data*
step then runs `gh release download "$GAMEDATA_RELEASE_TAG" --pattern tribesinstall.7z`. A direct
URL via the `GAMEDATA_URL` secret is supported as a fallback. To update the data later, upload a new
asset / bump the tag — no code change.

## Building on GitHub → GHCR

A workflow at `.github/workflows/build.yml` builds all three images and pushes them to the GitHub
Container Registry on push to `main`, on `v*` tags, or manually:
`ghcr.io/<owner>/<repo>/base`, `/classic`, `/construction` (tagged with the commit SHA and
`latest`). The `base` job runs first; a `mods` matrix job then builds Classic and Construction
`FROM` the freshly pushed base. The base build pulls game data as described above; the
Tribes 2 patch is fetched from `PATCH_URL` (overridable via the `PATCH_URL` repo
*variable*); the Classic mod zip and Construction installer **are** committed under `content/`.

Feasibility notes: the runner needs free disk for a multi-GB Wine image (the workflow frees
space first) and the build downloads the winetricks runtimes; expect a long first build.
GHCR auth uses the built-in `GITHUB_TOKEN` (no extra secret).

## TLS

The panel listens on HTTP (`HTTP_PORT`, default 8080) and, when enabled, HTTPS (`HTTPS_PORT`,
default 8443). Pick at most one cert source via env (see `.env.example`):

- `SELF_SIGNED_CERT=1` — generate (and persist to `/data`) a self-signed cert from
  `SELF_SIGNED_SUBJECT`/`SELF_SIGNED_CN`, `SELF_SIGNED_DNS`, `SELF_SIGNED_IP`, `SELF_SIGNED_DAYS`.
- `LETS_ENCRYPT_CERT=1` — provision/renew via ACME (LettuceEncrypt) from `LETS_ENCRYPT_EMAIL`
  and `LETS_ENCRYPT_DOMAINS` (DNS names; IP certs depend on ACME-server support). Needs the
  HTTP port reachable from the internet for the HTTP-01 challenge. `LETS_ENCRYPT_STAGING=1`
  while testing.
- Neither — plain HTTP; terminate TLS at an external reverse proxy.

## Open items to confirm for your deployment

- **`PATCH_URL`**: defaults to the 2025-09-22 preview installer; pin `PATCH_SHA256`
  for reproducibility.
- **Root seeding**: `PANEL_ROOT_PASSWORD` is plaintext, used once on first boot to create the
  root user (hashed by Identity). Change it in the panel afterward.
- **Game UDP ports** for your mod (default exposes 28000/udp).
- **Wine windows version**: prefix is `win7` (so UCRT installs); if the engine misbehaves,
  add a per-exe `winxp` override.
- **Auth/master**: `-online` registers with the Tribes 2 master; use `-nologin` to host
  standalone.
- **Transitive advisory**: EF Core 10 pins `SQLitePCLRaw … e_sqlite3 2.1.11` (GHSA-2m69-gcr7-jv3q).
  Low real-world risk for a local admin-only DB; clears when EF Core bumps the dependency.

## Layout

```
Dockerfile                 base (standard) image (panel build stage + wine runtime)
mods/
  classic/Dockerfile       Classic image (classic_v152 + TacoServer) FROM base
  construction/Dockerfile  Construction image (Construction_v0.70a) FROM base
docker-compose.yml         base + classic + construction services (profiles)
content/
  tribesinstall.7z         game data (GameData/ at archive root; not committed)
  classic_v152.zip         Classic v1.52 mod (committed)
  Construction_v0.70a.exe  Construction mod RAR self-extractor (committed)
  tribes_dual_patcher.py   PE patcher (AllocConsole NOP + GUI->CUI)
panel/                     ASP.NET Core 10 control panel (PID 1)
  Program.cs               host wiring: TLS, EF/Identity, RBAC, supervisor, endpoints
  Bootstrap.cs             EnsureCreated + seed roles/root
  Endpoints.cs             minimal-API: account, console SSE, lifecycle, users, audit
  Auth/                    ApplicationUser/Role + rank-based Roles/policies
  Data/                    AppDbContext (IdentityDbContext) + AuditEntry
  Services/                GameSupervisor (worker), ConsoleHub, TelnetCommander
  Tls/TlsConfigurator.cs   self-signed / Let's Encrypt / plain HTTP from env
  ClientApp/               React + Vite + TS SPA (built into wwwroot)
.github/workflows/build.yml  CI: build + push base/classic/construction to GHCR
```
