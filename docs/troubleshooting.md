# Troubleshooting

## The panel is up but the game won't start

The game runs **only** when the server is **configured** and either Auto-Start is on or you start
it manually:

- Log in as **root** and complete **first-time setup**, or
- On **Controls**, click **Start** (Admin+), and check **Auto-Start** (root) for future boots.

Watch the **Console** page and the status bar (`state`, `ruleset`, final `params`). The supervisor
also publishes `[panel] …` lines for launch/exit events.

## I can't log in

- The **root** user is seeded **once** on first boot and requires `ROOT_PASSWORD` (or
  `PANEL_ROOT_PASSWORD`). If the DB volume already existed without a root user and no password was
  set, no root is created — set the env var and restart, or seed via a fresh `/data`.
- Auth cookies are signed by Data-Protection keys under `/data`. If you wiped `/data` or didn't
  persist it, existing sessions are invalid — log in again.

## "Setup required" forever / non-root users locked out

Only **root** can complete first-time setup. Log in as root. Until then everyone else correctly
sees the notice.

## The ruleset/`-mod` isn't applied

- Empty or `base` means **no `-mod`** by design.
- The panel setting (set in setup or **Controls**) **overrides** the `SERVER_RULESET` env. Check
  **Controls → Ruleset / Mod** and the status bar's final `params`.
- A ruleset change takes effect on the **next restart** — click **Restart**.
- For a custom ruleset, the folder must exist at `GameData/<name>/` (with `scripts/`). If it's
  missing, the game will fail to load the mod — bake it ([custom mod image](custom-mod-image.md))
  or upload it.

## My custom ruleset doesn't appear in the dropdown

Discovery lists only `GameData` folders that contain a **`scripts/`** subdir (and the name is
case-sensitive on Linux). You can still **type** the name manually. See
[Rulesets & mods](rulesets-and-mods.md).

## Uploaded/edited a file but it's "outside GameData" (403)

Developers are scoped to the **GameData** tree; only **root** can touch paths elsewhere. Paths are
canonicalized, so `../` escapes are rejected. Use a root account for system paths, or the
**Terminal**.

## The game keeps crash-looping

Open **Crashes** (Admin+) for the start/crash timestamps, exit code, and (for access violations)
the fault address/instruction + console tail + `CRASHLOG.TXT`. To stop the loop while you
investigate, set `RESTART_ON_CRASH=false` (the supervisor stays down after an exit).

## HTTPS isn't listening

HTTPS is bound only when `SELF_SIGNED_CERT=1` or `LETS_ENCRYPT_CERT=1`. For Let's Encrypt the
domain must resolve here and the HTTP port must be internet-reachable (HTTP-01). See [TLS](tls.md).

## CI build fails: "No game data source"

The base image needs `content/tribesinstall.7z`. In CI, set the `GAMEDATA_RELEASE_TAG` repo
variable (a Release asset) or the `GAMEDATA_URL` secret. See
[Building & deploying](building-and-deploying.md#providing-game-data-to-ci).

## Inspecting from the inside

- **Terminal** page (root) gives you an interactive shell in the container.
- Or `docker exec -it <container> bash`.
- Check the game files: `ls "$GAME_DIR"`, `ls "$GAME_DIR/base"/*.vl2`, `cat "$GAME_DIR/CRASHLOG.TXT"`.
- Inspect the DB: `sqlite3 /data/panel.db .tables` (see [Database](database.md)).

## See also
- [Web panel & roles](web-panel.md) · [Internals](internals.md) · [Configuration reference](configuration.md)
- Back to [docs index](README.md)
