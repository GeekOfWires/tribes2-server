---
title: Home
nav_order: 1
---

# Tribes 2 Dedicated Server — Documentation

This folder documents the Tribes 2 dedicated-server container and its ASP.NET Core
control panel: how it is put together, how to run and configure it, and — the part most
people come here for — **how to build your own mod image with a custom ruleset**.

> New to the project? Read [Architecture](architecture.md) for the big picture, then jump to
> [Creating a custom mod image](custom-mod-image.md).

## Contents

| Doc | What's in it |
|-----|--------------|
| [Architecture](architecture.md) | How the pieces fit: ASP.NET Core panel as image owner, Wine, the headless game, the PTY bridge, the database. |
| [Configuration reference](configuration.md) | Every environment variable and build arg, with defaults. |
| [Web panel & roles](web-panel.md) | Every page in the panel, the role/permission model, and the Developer capability. |
| [Rulesets & mods](rulesets-and-mods.md) | How `SERVER_RULESET` / `-mod` works and how the panel discovers and configures rulesets. |
| [**Creating a custom mod image**](custom-mod-image.md) | **Step-by-step: derive from the base image, install your mod, set the ruleset, build, compose, CI.** |
| [Building & deploying](building-and-deploying.md) | Local builds, Docker Compose, GHCR images, the GitHub Actions workflow, VC++ runtime licensing. |
| [Networking & client IPs](networking.md) | Make real player IPs reach the container (host networking / DNAT) so bans & admin work. |
| [Internals: patch & headless](internals.md) | The PE patch, the no-xvfb headless approach, the PTY bridge, the modern-runtime DLLs, crash tracking. |
| [Database](database.md) | The local Turso/SQLite database, its tables, and how schema changes are applied. |
| [TLS](tls.md) | Self-signed and Let's Encrypt certificates. |
| [Troubleshooting](troubleshooting.md) | Common problems and how to diagnose them. |

## At a glance

- **One image, two layers of concern.** The image is fundamentally an **ASP.NET Core 10**
  app; Wine + the Tribes 2 game are layered on top. The panel is **PID 1** and *owns* the
  game via a hosted Worker Service, so the panel stays up even when the game stops or crashes.
- **Headless, no xvfb.** The dedicated server runs without a display via a PE patch
  (GUI→console subsystem) and a pseudo-terminal bridge. See [Internals](internals.md).
- **Configure from the panel.** First-time setup, Auto-Start, ruleset/`-mod`,
  `serverprefs.cs`, file editing (Monaco), uploads, a root container terminal, users, audit,
  and crash reports — all in the browser. See [Web panel & roles](web-panel.md).
- **Mods are folders + a ruleset name.** A "ruleset/mod" is a top-level `GameData` folder
  (e.g. `Classic`) with a `scripts/` dir. Bake it into a derived image *or* upload it at
  runtime, then point `SERVER_RULESET`/the panel at it. See
  [Creating a custom mod image](custom-mod-image.md).

The top-level [README](https://github.com/GeekOfWires/tribes2-server/blob/main/README.md) has the quick start; these docs go deeper.
