---
title: Rulesets & mods
nav_order: 5
---

# Rulesets & mods

A **mod** (a.k.a. **ruleset**) in Tribes 2 is a top-level folder inside `GameData` — e.g.
`base`, `Classic`, `Construction`, or your own `MyMod` — that the engine loads with the
**`-mod <name>`** launch parameter. `base` is the core game and needs no `-mod`.

This project separates two concerns that used to be tangled together:

1. **The files** — a mod's scripts/prefs/assets must exist under `GameData/<name>/`. These are
   either **baked into an image** at build time (the Classic/Construction images do this) or
   **uploaded at runtime** through the panel.
2. **The selection** — which mod to actually run, i.e. the `-mod` parameter. This is driven by
   **`SERVER_RULESET`** and can be overridden in the panel.

Keeping them separate means one image can host different rulesets, and you can add a new
ruleset without rebuilding.

## How `-mod` is composed

The supervisor takes `LAUNCH_PARAMS` and, if a ruleset is selected, **appends `-mod <ruleset>`
at the very end**. This matters: the engine's `console_start.cs` `-mod` handler advances the
argument index by two (not one), so it *swallows the argument that follows the mod name*.
Putting `-mod <ruleset>` last means it can only eat a trailing nothing — an earlier
`-dedicated` is safely parsed. (Retail's Classic launcher runs `-dedicated -mod Classic` for
the same reason; `-mod Classic -dedicated` eats `-dedicated` and the server boots as a headless
*client*, which then crashes on video init.)

| `LAUNCH_PARAMS` | `SERVER_RULESET` | Final command line |
|-----------------|------------------|--------------------|
| `-online -dedicated` | `""` or `base` | `-online -dedicated` (no `-mod`) |
| `-online -dedicated` | `Classic` | `-online -dedicated -mod Classic` |
| `-online -dedicated` | `MyMod` | `-online -dedicated -mod MyMod` |

Rules:
- **Empty or `base`** (any case) → **no `-mod`**.
- If you already put an explicit `-mod` in `LAUNCH_PARAMS`, it's respected and nothing extra is
  inserted.

## Where the ruleset value comes from (precedence)

1. **Panel setting** (persisted in the DB) — set during first-time setup or later from
   **Controls**. Once set, this wins.
2. **`SERVER_RULESET` env** — the default. The base image leaves it empty; the Classic and
   Construction images set it to `Classic`/`Construction`.

So a derived image ships a sensible default, and operators can still change it without
rebuilding.

## Configuring it in the panel

- **First-time setup** shows a ruleset field defaulting to `SERVER_RULESET`, plus an inline
  `serverprefs.cs` editor for the chosen ruleset.
- **Controls → Ruleset / Mod** (root) changes it later; it takes effect on the **next restart**.
- Both fields are **combo boxes**: the panel calls `GET /api/config/rulesets` to **discover
  installed rulesets** (top-level `GameData` folders that contain a `scripts/` dir) and offers
  them as suggestions, while still letting you **type a new name**.

## serverprefs.cs

Each ruleset has its own server preferences at `GameData/<base|ruleset>/prefs/serverprefs.cs`.
During setup (and via the Files editor) root can edit it; the panel creates the `prefs/` dir if
it doesn't exist. This is plain TorqueScript — set `$Host::Name`, `$Host::Password`,
`$Host::MaxPlayers`, map rotation, etc.

**Build-time default.** Each image seeds its ruleset's `serverprefs.cs` with `$Host::Linux = 1;`
(base seeds `base/prefs`, the Classic/Construction images seed theirs too) via the baked helper
`/usr/local/bin/set-serverprefs-defaults.sh`. Your edits are layered on top of that default.

## Two ways to add a ruleset

### A) Bake it into a derived image (recommended for distribution)
Best when you want a reproducible, shippable server for a specific mod. This is what the
Classic/Construction images do. **→ [Creating a custom mod image](custom-mod-image.md).**

### B) Upload it at runtime (quick / experimental)
Best for trying something or adding a ruleset to a running server:

1. In the panel **Files** page (as a Developer or root), navigate to `GameData`.
2. Create the mod folder (e.g. `MyMod`) and **upload** its files into it (scripts, prefs,
   assets). Maps go as `.vl2` files **directly** in `GameData/base/` (the engine only mounts
   `.vl2` placed directly in `base/`, not in subfolders).
3. Go to **Controls → Ruleset / Mod**, type `MyMod`, **Apply**, then **Restart**.

Runtime uploads are audited (and revertible) like any other file change.

## See also
- [**Creating a custom mod image**](custom-mod-image.md)
- [Web panel & roles](web-panel.md) · [Configuration reference](configuration.md)
- Back to [docs index](README.md)
