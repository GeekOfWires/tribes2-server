---
title: Configuration reference
nav_order: 3
---

# Configuration reference

Everything is configured through **environment variables** (read directly by the panel and the
supervisor) and a few **build args** (baked at image build time). With Docker Compose, copy
[`.env.example`](https://github.com/GeekOfWires/tribes2-server/blob/main/.env.example) to `.env` and edit.

## Build args (image build time)

| Arg | Default | Purpose |
|-----|---------|---------|
| `PATCH_URL` | the community QoL patch URL | NSIS installer `.exe`; its payload is 7z-extracted over `GameData`. |
| `PATCH_SHA256` | *(empty)* | If set, the downloaded patch is checksum-verified. |
| `VCREDIST_URL` | `https://aka.ms/vs/17/release/vc_redist.x86.exe` | Microsoft's **official** VC++ 2022 x86 redistributable, fetched at build time and unpacked with `cabextract`; the runtime DLLs land in the Wine `system32`. |
| `VCREDIST_SHA256` | *(empty)* | If set, the downloaded redist is checksum-verified. |
| `WINE_BRANCH` | `stable` | WineHQ branch (`stable`/`staging`/`devel`). |
| `WINE_VERSION` | `10.0.0.0` | Pinned Wine version (Wine 11 regresses the mission-start path). Blank = latest for the branch. |
| `BASE_IMAGE` | `tribes2-server:base` | *(mod images only)* the base image to derive from. |

The VC++ runtime is fetched **from Microsoft** at build time (so Microsoft is the distributor and
no Microsoft binaries are committed to this repo). See
[Building & deploying → licensing](building-and-deploying.md#a-note-on-the-vc-runtime-licensing).

See [Building & deploying](building-and-deploying.md) for how to pass these.

## Runtime environment variables

### Game launch & ruleset

| Var | Default | Purpose |
|-----|---------|---------|
| `LAUNCH_PARAMS` | `-online -dedicated` | Base launch parameters. `-online` (or `-nologin`) first, `-dedicated` last. |
| `SERVER_RULESET` | `""` (base image) | Selects `-mod <ruleset>`. Empty or `base` = no `-mod`. Mod images default this (`Classic`/`Construction`). See [Rulesets & mods](rulesets-and-mods.md). |
| `GRACE_SECONDS` | `20` | Seconds to wait for a graceful `quit();` before killing the process tree. |
| `RESTART_BACKOFF` | `5` | Seconds to wait before relaunching after an exit. |
| `RESTART_ON_CRASH` | `true` | If false, the supervisor stays down after the game exits. |
| `GAME_CPU_AFFINITY` | *(unset)* | If set (e.g. `0-3`), the game is launched under `taskset -c`. |
| `CONSOLE_RING` | `1000` | Lines of console history kept in memory for new SSE subscribers. |

> The panel can override `LAUNCH_PARAMS` and the ruleset at runtime (persisted in the DB); the
> env values are the **defaults** offered during first-time setup.

### Paths (rarely changed)

| Var | Default |
|-----|---------|
| `GAME_DIR` | `/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData` |
| `WINEPREFIX` | `/opt/wineprefix` |
| `WINEARCH` | `win32` |
| `WINE_BIN` | `wine` |
| `EXE_PATH_WIN` | `C:\Dynamix\Tribes2\GameData\Tribes2.exe` |
| `WINEDEBUG` | `-all` |
| `WINEDLLOVERRIDES` | *(set in the Dockerfile for the vcrun DLLs)* |

### Panel: ports, database, root user

| Var | Default | Purpose |
|-----|---------|---------|
| `HTTP_PORT` | `8080` | Panel HTTP port (always on; also used for ACME HTTP-01). |
| `HTTPS_PORT` | `8443` | Panel HTTPS port (only bound if a cert is configured). |
| `PANEL_DB_PATH` | `/data/panel.db` | SQLite/Turso-compatible database file. |
| `DATAPROTECTION_DIR` | `<db dir>/keys` | Where ASP.NET Data Protection keys are persisted (so cookies survive restarts). |
| `ROOT_USERNAME` | `root` | Initial root username (also accepts `PANEL_ROOT_USERNAME`). |
| `ROOT_PASSWORD` | *(required on first boot)* | Initial root password (also accepts `PANEL_ROOT_PASSWORD`). Needed to seed the first root user. |

> The root user is seeded **once**, on first boot, only if no root exists. After that,
> `ROOT_PASSWORD` is ignored — manage users in the panel.

### TLS
See [TLS](tls.md) for details and examples.

| Var | Default | Purpose |
|-----|---------|---------|
| `SELF_SIGNED_CERT` | `0` | `1` to generate/persist a self-signed cert from `SELF_SIGNED_*`. |
| `SELF_SIGNED_SUBJECT` | *(empty)* | e.g. `CN=tribes2.example.com` (overrides `SELF_SIGNED_CN`). |
| `SELF_SIGNED_CN` | `tribes2-panel` | Common name if no subject given. |
| `SELF_SIGNED_DNS` | *(empty)* | Comma list of DNS SANs. |
| `SELF_SIGNED_IP` | *(empty)* | Comma list of IP SANs. |
| `SELF_SIGNED_DAYS` | `365` | Validity. |
| `SELF_SIGNED_PASSWORD` | *(empty)* | PFX password for the persisted cert. |
| `SELF_SIGNED_PATH` | `/data/self-signed.pfx` | Where the cert is persisted. |
| `LETS_ENCRYPT_CERT` | `0` | `1` to provision via ACME (LettuceEncrypt). |
| `LETS_ENCRYPT_EMAIL` | *(empty)* | ACME account email. |
| `LETS_ENCRYPT_DOMAINS` | *(empty)* | Comma list of domains. |
| `LETS_ENCRYPT_STAGING` | `0` | `1` to use the Let's Encrypt staging CA. |
| `LETS_ENCRYPT_CERT_DIR` | `/data/letsencrypt` | Where ACME state/certs are persisted. |
| `LETS_ENCRYPT_PFX_PASSWORD` | *(empty)* | Password for persisted ACME PFX. |

## Ports

| Port | Proto | What |
|------|-------|------|
| `8080` | tcp | Panel HTTP |
| `8443` | tcp | Panel HTTPS (if a cert is configured) |
| `28000` | udp | Tribes 2 game traffic |

## Persisting data

Mount a volume at **`/data`** to persist the database, Data-Protection keys, and TLS material
across container recreation. Compose does this per service (`base-data`, `classic-data`, …).

## See also
- [Building & deploying](building-and-deploying.md) · [TLS](tls.md) · [Rulesets & mods](rulesets-and-mods.md)
- Back to [docs index](README.md)
