---
title: Web panel & roles
nav_order: 4
---

# Web panel & roles

The panel is a React SPA served by the ASP.NET Core host. Authentication is cookie-based
(ASP.NET Core Identity); authorization is a **rank comparison** plus one orthogonal capability.

## Roles & permissions

Roles have an integer **rank**; a higher rank inherits everything below it.

| Role | Rank | Can… |
|------|------|------|
| **User** | 10 | View the live server console; view status. |
| **Admin** | 20 | + **Restart** (graceful `quit();`, auto-relaunch) / **Start**; view **Crash Reports**. |
| **Super Admin** | 30 | + Graceful **Stop** + **Force-restart** (emergency kill + relaunch) + run **console commands**; view the **Audit log**. |
| **root** | 40 | + **Force-shutdown the panel** (stops the container), **user management**, edit **any** file, **container terminal**, file-change **history + revert**, first-time **setup** and ruleset/Auto-Start config. |

### The Developer capability
**Developer** is **not a rank** — it's an additive flag a root user assigns to *any* User/Admin/
Super Admin. It grants the **Files** editor + **upload**, scoped to the **GameData** tree.
root holds it implicitly and is unrestricted (any path in the container).

| Capability | Developer | root |
|------------|-----------|------|
| Browse/edit/upload under `GameData` | ✅ | ✅ |
| Browse/edit/upload **anywhere** in the container | ❌ | ✅ |
| File **history & revert** | ❌ | ✅ |
| Container **terminal** | ❌ | ✅ |

Path access is canonicalized server-side, so a `../` escape out of `GameData` is rejected for
Developers.

## First-time setup & Auto-Start

On a fresh database the server is **unconfigured** and does not run. Log in as **root**
(seeded from `ROOT_USERNAME`/`ROOT_PASSWORD`) and the panel shows **first-time setup**:

- choose **launch parameters**,
- choose the **ruleset/mod** (defaults to `SERVER_RULESET`; see [Rulesets & mods](rulesets-and-mods.md)),
- optionally edit **`serverprefs.cs`** for that ruleset right there in the Monaco editor,
- toggle **Auto-Start**.

Completing setup marks the server **configured** and starts it. On every panel startup the game
is launched automatically **only when Auto-Start is on**. Non-root users see a "setup required"
notice until then. root can change Auto-Start and the ruleset later from **Controls**.

## Pages

| Page | Who | What |
|------|-----|------|
| **Console** | User+ | Live console (SSE), status bar (state, pid, ruleset, final launch line, restarts). |
| **Controls** | Admin+ | Lifecycle buttons; Super Admin gets force-restart/stop + a console command box; root gets Auto-Start, ruleset, and the panel-shutdown danger zone. |
| **Crashes** | Admin+ | Read-only crash reports (see [Internals → crash tracking](internals.md#crash-tracking)). |
| **Files** | Developer / root | File browser + **Monaco** editor (VS Code Dark+, TorqueScript + common config languages) + create/delete/**upload**. |
| **Terminal** | root | Interactive `bash` on a real PTY (xterm.js over WebSocket). |
| **File History** | root | Every panel file change, with **Revert**. |
| **Users** | root | Create/delete users, set role, activate/deactivate, reset password, toggle **Developer**. |
| **Audit Log** | Super Admin+ | All privileged actions. |

## API surface (for reference / automation)

All under `/api`. Auth is the login cookie; the policy column is the minimum.

| Method & path | Policy | Notes |
|---------------|--------|-------|
| `POST /account/login` · `POST /account/logout` · `GET /account/me` | — / auth | Login returns `{userName, role, rank, isDeveloper}`. |
| `GET /console/stream` | User | Server-Sent Events console feed. |
| `GET /server/status` | User | State, pid, `ruleset`, final `params`, restarts. |
| `POST /server/restart` · `/server/start` | Admin | |
| `POST /server/force-restart` · `/server/stop` · `/server/command` | Super Admin | |
| `GET /config/` | User | `configured, autoStart, launchParams, defaultLaunchParams, ruleset, defaultRuleset`. |
| `GET /config/rulesets` | User | Discovered rulesets (GameData folders with `scripts/`). |
| `POST /config/complete` · `/config/auto-start` · `/config/ruleset` | root | |
| `GET /config/serverprefs?ruleset=` | root | Resolves `GameData/<base|ruleset>/prefs/serverprefs.cs` (creates the dir). |
| `GET/POST /users/...` (`/{id}/role`,`/active`,`/password`,`/developer`, `DELETE /{id}`) | root | |
| `GET /audit` | Super Admin | |
| `GET /crashes` | Admin | |
| `GET /files/list` · `/files/read` | User\* | \*plus scope: Developer under GameData, root anywhere. |
| `POST /files/save` · `/files/create` · `/files/delete` · `/files/upload` | User\* | Same scope; each write is audited. |
| `GET /files/edits/` · `POST /files/edits/{id}/revert` | root | File-change history + revert. |
| `GET /api/terminal/ws` | root | WebSocket; interactive shell. |
| `POST /panel/shutdown` | root | Stops the host/container. |

## See also
- [Rulesets & mods](rulesets-and-mods.md) · [Database](database.md) · [TLS](tls.md)
- Back to [docs index](README.md)
