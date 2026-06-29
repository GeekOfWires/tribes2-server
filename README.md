# Tribes 2 Dedicated Server Container

<img src="panel/ClientApp/public/icon.svg" alt="Tribes 2 server emblem" width="104" align="right" />

A single Docker image that runs a **Tribes 2** (2002) dedicated server under **Wine** on
**Debian**, applies the **Tribes 2** community patch, and ships an integrated
**ASP.NET Core 10** control panel (React SPA frontend, ASP.NET Identity auth) with
role-based access. The panel is **PID 1** and owns the game lifecycle.

> **Engine note:** Tribes 2 runs on the Dynamix **"V12" engine** — the precursor to the
> Torque Game Engine (TGE), which preceded Torque Game Engine Advanced (TGEA), which preceded
> Torque 3D.

## 📚 Documentation

Published as a site at **<https://geekofwires.github.io/tribes2-server/>** (built from
[`docs/`](docs/README.md) by [`.github/workflows/pages.yml`](.github/workflows/pages.yml)).

> One-time setup to turn the site on: repo **Settings → Pages → Build and deployment →
> Source = "GitHub Actions"**. The workflow then deploys on every push that touches `docs/`.

In-depth guides:

- [Architecture](docs/architecture.md) — how the panel, Wine, the game, and the database fit together.
- [**Creating a custom mod image**](docs/custom-mod-image.md) — **build your own server image with a custom mod + ruleset** (worked examples).
- [Rulesets & mods](docs/rulesets-and-mods.md) — how `SERVER_RULESET` / `-mod` works; baking vs uploading.
- [Web panel & roles](docs/web-panel.md) — every page, the role model, the Developer capability, the API.
- [Configuration reference](docs/configuration.md) — every env var and build arg.
- [Building & deploying](docs/building-and-deploying.md) · [Internals: patch & headless](docs/internals.md) · [Database](docs/database.md) · [TLS](docs/tls.md) · [Troubleshooting](docs/troubleshooting.md)

## What the build does

1. `FROM mcr.microsoft.com/dotnet/aspnet:10.0` — the **ASP.NET Core runtime owns the image**;
   Wine + the game are layered on top. (.NET has no linux-x86 runtime, so the image is amd64
   with **i386 multiarch** for 32-bit Wine — not a pure-i386 base.)
2. Installs **WineHQ** (`winehq-stable`, **pinned to Wine 10** via `WINE_VERSION` — Wine 11
   regresses the T2 mission-start path) with 32-bit components and initializes a `win32`
   Wine prefix **headlessly** (`wineboot --init`; no winetricks, no xvfb).
3. Provides the Windows runtimes the way the Tribes 2 Linux community does
   ([ChocoTaco1/docker-tribesnext-server](https://github.com/ChocoTaco1/docker-tribesnext-server/tree/Wine)):
   - **Old VC++6 runtime** — the game's own bundled `MSVCRT.dll` + `Tribes2.exe.local`
     (DLL redirection), already in `GameData`.
   - **Newer 32-bit Windows APIs** (what the QoL patch needs) — the real Microsoft **VC++ 2022**
     DLLs (`vcrun22`: `msvcp140`, `vcruntime140`, `concrt140`, …) dropped into `system32`,
     with native DLL-overrides. No Ruby — the 2025 QoL is native code inside `IFC22.dll`.
4. Extracts `content/tribesinstall.7z` so `GameData` lands at `C:\Dynamix\Tribes2\GameData`.
5. Downloads the **Tribes 2 QoL patch** (`PATCH_URL` build ARG — the "variable";
   defaults to `TribesNEXT_20250922_preview.exe`). It's an **NSIS installer**, so `7z`
   extracts its payload deterministically (`IFC22.dll`, Miles sound libs, SDL3/OpenAL/
   Discord/libcurl, `base/t2csri.vl2`, …) straight onto `GameData` — **no Wine-run required**.
6. Runs the Python PE patcher (`content/tribes_dual_patcher.py`) over `Tribes2.exe`:
   flips the PE subsystem GUI→CUI so the dedicated server is a console app — its console
   output goes to stdout and its console input is read from stdin. The supervisor then runs
   it on a **PTY** (a real TTY, still headless) so the engine's `ReadConsoleInput` console
   works with no display.
7. Adds the framework-dependent **ASP.NET Core 10 panel** (with the built React SPA) and sets
   it as the entrypoint (PID 1).

> Why the PTY: on a plain pipe, `ReadConsoleInput` fails and the server crashes at "starting
> mission countdown" (root-caused to the per-tick console-input poller over-reading an
> uninitialized event count). A TTY fixes that **and** gives us a command channel — no xvfb,
> no telnet (the engine's `telnetSetParameters` is fatal in this head-less build).

## Runtime architecture (single container)

```
PID 1: ASP.NET Core panel (Kestrel + React SPA + ASP.NET Identity)
  └─ GameSupervisor (hosted Worker Service)
       └─ python PTY bridge ─ wine Tribes2.exe $LAUNCH_PARAMS
            ├─ game stdout (PTY) -> bridge strips ANSI -> ConsoleHub ring buffer + SSE
            ├─ game stdin  (PTY) <- panel console commands + quit();
            └─ lifecycle: restart / force-restart / stop / start + internal auto-restart

browser --HTTPS+cookie (Identity)--> panel API/SSE --> GameSupervisor --> game
```

- **The panel owns the lifecycle.** The game runs inside the panel process; crashes are
  auto-restarted internally so the panel stays available. `restart: on-failure` only relaunches
  the *container* if the panel itself exits (e.g. root's force-shutdown).
- **Console feed** = the game's console output (over the PTY), ANSI-stripped, streamed via SSE.
- **Commands / `quit();`** = written to the game's console **stdin** over the PTY (no telnet).
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
| root         | + **Force-shutdown the panel** (stops the container; restart policy relaunches it) + **user management** + edit **any** file + a **container terminal** |

**Developer** is an *additive capability*, not a rank — root can grant it to any User/Admin/Super
Admin. It unlocks the **Files** editor scoped to the **GameData** tree. root holds it implicitly
and is unrestricted (any path in the container).

All privileged actions are recorded in an **audit log** (visible to Super Admin+).

Every unexpected/unhandled game exit (access violations) is recorded to a read-only
**Crash Reports** page (Admin+): server start + crash timestamps, exit code, the `0x` fault
address, faulting instruction, module, launch params, and the console tail + `CRASHLOG.TXT`,
so hosts can report reproducible crashes for the image to patch.

## Files, editing & terminal

The **Files** page is a Monaco editor (VS Code **Dark+** theme, with **TorqueScript** highlighting
for the engine's `.cs`/`.gui`/`.mis` scripts plus shell/ini/yaml/json/etc.). Developers browse and
edit under **GameData**; root anywhere. **Every change is written to a `FileEdits` table** with the
pre-change snapshot, so root can **revert** any edit/create/delete from the **File History** page.
Path access is canonicalized and scope-checked server-side (a `../` escape out of GameData is denied).

The Files page also supports **uploads** (multipart): Developers may upload into the GameData
tree (any depth), root anywhere. Each uploaded file is audited like an edit.

root also gets a **Terminal** page — an interactive `bash` session on a real PTY inside the container
(xterm.js over a WebSocket), so `vim`, `htop`, etc. work. Terminal sessions are audited.

## First-time setup & Auto-Start

On a fresh database the server is **unconfigured** and does **not** run. Log in as **root**
(`ROOT_USERNAME`/`ROOT_PASSWORD`, seeded on first boot) — the panel shows a **first-time setup**
screen. Completing it (choose launch params + whether to Auto-Start) marks the server
**configured** in SQLite and starts it. Afterwards root can toggle **Auto-Start** any time; the
flag is persisted, and on every panel startup the ASP.NET host launches the game automatically
only when Auto-Start is `true`. Non-root users see a "setup required" notice until then.

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

| Image | Mod | Builds from | `SERVER_RULESET` |
|-------|-----|-------------|------------------|
| `tribes2-server:base` | base | [Dockerfile](Dockerfile) | *(empty → no `-mod`)* |
| `tribes2-server:classic` | Classic | [mods/classic/Dockerfile](mods/classic/Dockerfile) | `Classic` |
| `tribes2-server:construction` | Construction | [mods/construction/Dockerfile](mods/construction/Dockerfile) | `Construction` |

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

**Rulesets / `-mod`.** The derived Classic/Construction images still **install their mod files
at build time** (the whole point of a baked image); what changed is only how the `-mod`
parameter is *selected*. The **`SERVER_RULESET`** env picks the ruleset and the supervisor
inserts `-mod <ruleset>` between `-online` and `-dedicated`. Empty or `base` means **no** `-mod`.
The derived images set this env (`Classic`/`Construction`); the base image leaves it empty.

root can also set the ruleset in the panel — during **first-time setup** (defaulting to the
`SERVER_RULESET` value) and later from **Controls** (applied on the next restart). The panel
**suggests the installed rulesets** it discovers (top-level `GameData` folders containing a
`scripts/` dir — `base` plus any baked or uploaded mod) and lets you **type a newer ruleset**:
upload its files via the Files page, then enter its name. So the baked images and ad-hoc
rulesets coexist.

`LAUNCH_PARAMS` ordering still matters for anything you put there: `-online` (or `-nologin`
to host outside WON/Tribes 2 auth) **first**, `-dedicated` **last**.

During first-time setup root also edits **`serverprefs.cs`** for the chosen ruleset
(`GameData/<base|ruleset>/prefs/serverprefs.cs`, created if missing) right in the Monaco
editor; the save goes through the audited file pipeline.

## Verifying the patch (inside the built image)

```bash
docker run --rm --entrypoint bash tribes2-server:base -c '
  ls -l "$GAME_DIR/IFC22.dll";                       # ~2 MB (Tribes 2)
  python3 /opt/patcher/tribes_dual_patcher.py --exe "$GAME_DIR/Tribes2.exe" --dry-run'
# "Current subsystem: CUI" confirms the headless console patch is applied.
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
space first) and the build installs Wine + downloads the VC++ runtime; expect a long first build.
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
- **Root seeding**: `ROOT_PASSWORD` (plaintext) is used once on first boot to create the root
  user (hashed by Identity). Change it in the panel afterward.
- **Game UDP ports** for your mod (default exposes 28000/udp).
- **VC++ runtime**: the modern VC++ 2022 DLLs are fetched from **Microsoft's official
  redistributable** (`VCREDIST_URL`, default `aka.ms/vs/17/release/vc_redist.x86.exe`) at build
  time and unpacked with `cabextract` — nothing is vendored in the repo. See
  [docs: licensing](docs/building-and-deploying.md#a-note-on-the-vc-runtime-licensing).
- **CPU pinning**: set `GAME_CPU_AFFINITY` (e.g. `0`) to `taskset` the single-threaded server
  onto one core.
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
  tribes_dual_patcher.py   PE patcher (subsystem GUI->CUI for headless console I/O)
panel/                     ASP.NET Core 10 control panel (PID 1)
  Program.cs               host wiring: TLS, EF/Identity, RBAC, supervisor, endpoints
  Bootstrap.cs             EnsureCreated + seed roles/root
  Endpoints.cs             minimal-API: account, console SSE, lifecycle, users, audit
  Auth/                    ApplicationUser/Role + rank-based Roles/policies
  Data/                    AppDbContext (IdentityDbContext) + AuditEntry
  Services/                GameSupervisor (worker; PTY bridge + stdin commands), ConsoleHub
  Tls/TlsConfigurator.cs   self-signed / Let's Encrypt / plain HTTP from env
  ClientApp/               React + Vite + TS SPA (built into wwwroot)
.github/workflows/build.yml  CI: build + push base/classic/construction to GHCR
```
