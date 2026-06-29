---
title: Internals
nav_order: 8
---

# Internals: patch & headless

How a 2002 Windows game runs **headless under Wine with no xvfb**, why the modern runtime DLLs
are needed, and how crashes are captured.

## Running a GUI game with no display

Tribes 2's `Tribes2.exe` is a **GUI subsystem** PE. Even a dedicated server normally expects a
window/display. Two things make it run truly headless:

1. **PE subsystem patch (GUI → console).** A build-time Python patcher
   ([`content/tribes_dual_patcher.py`](https://github.com/GeekOfWires/tribes2-server/blob/main/content/tribes_dual_patcher.py)) flips the PE header's
   subsystem from **GUI (2)** to **console/CUI (3)** for `Tribes2.exe`. This makes the process a
   console app so it has real standard streams. It's a one-field PE edit (verified by re-reading
   the header), with a backup.
2. **A pseudo-terminal (PTY).** The dedicated-server code path polls console input
   (`GetNumberOfConsoleInputEvents` / `ReadConsoleInput`). With no real console that path reads
   uninitialized state and faults at mission start. Instead of removing it, the supervisor gives
   the game a **real TTY**: it launches the game through a tiny embedded **Python PTY bridge**, so
   console input/output behave correctly.

The result: **no xvfb, no virtual X server, no telnet** — the game just runs on a pseudo-terminal.

## The PTY bridge

`GameSupervisor` writes a small Python bridge to a temp path and launches:

```
python3 <bridge> [taskset -c <affinity>] wine <Tribes2.exe> <params...>
```

The bridge `openpty()`s, forks, makes the child a session leader with the slave as its
controlling terminal, and `dup2`s the slave to stdin/stdout/stderr before exec'ing the game.
The parent proxies bytes between the PTY master and its own stdio (stripping terminal escape
sequences). The supervisor then:

- reads the **console feed** from the bridge's stdout → publishes to `ConsoleHub` → SSE clients;
- writes **commands** (including `quit();`) to the bridge's stdin → the game's console input.

This is also why the **root terminal** works (a separate bridge runs `bash` instead of the game,
without escape-stripping). See [`Services/TerminalSession.cs`](https://github.com/GeekOfWires/tribes2-server/blob/main/panel/Services/TerminalSession.cs).

## Old + new Windows runtimes side by side

The game itself is a 2002 MSVC6-era binary; the QoL patch needs **newer** 32-bit Windows APIs.
Both must be satisfied in the Wine prefix:

- **Old VC++6 runtime** — comes from the game's own bundled `MSVCRT.dll` plus a
  `Tribes2.exe.local` redirection (so Wine loads the local copy).
- **New VC++ 2022 runtime** — real Microsoft DLLs (`msvcp140`, `vcruntime140`, `concrt140`, …)
  come from [`content/vcrun22.zip`](https://github.com/GeekOfWires/tribes2-server/blob/main/content/vcrun22.zip) (a `vcrun22.zip` of `*.dll_x86`,
  vendored in-repo, originally from files.playt2.com), renamed to `.dll` and dropped into the Wine
  `system32`, with `WINEDLLOVERRIDES` set to prefer the native copies.

No **winetricks** and no **Ruby** are involved — the 2025 QoL feature set is native code in
`IFC22.dll` (≈2 MB), overlaid from the patch.

## Wine version pin

Wine is pinned to **10.0.0.0** (`WINE_VERSION`). Wine 11 regresses the Tribes 2 mission-start
path. The pin selects matching `winehq-stable` / `wine-stable*` packages from the WineHQ repo for
the image's distro/codename.

## Build pipeline (Dockerfile stages)

1. **spa-build** (`node`) — builds the React SPA → `/wwwroot`.
2. **app-build** (`dotnet/sdk`) — restores + publishes the panel (framework-dependent) and copies
   in the SPA.
3. **runtime** (`dotnet/aspnet`, Ubuntu) — i386 multiarch + WineHQ + Wine; `wineboot` headless;
   drop the vcrun22 DLLs; bind-mount + extract the game; overlay the QoL patch; run the PE
   patcher; copy in the published panel. Entrypoint = the panel under `tini`.

## Crash tracking

When the game exits **unexpectedly** (i.e. not from an operator action — graceful quit,
force-restart, stop, or panel shutdown all mark the exit as expected), the supervisor records a
**crash report**:

- **server start** + **crash** timestamps and the process **exit code**;
- if the console tail contains an access violation, the parsed **module**, **`0x` fault address**,
  and **faulting instruction**;
- the **console tail** plus `CRASHLOG.TXT` from the game dir, for context.

These appear on the read-only **Crashes** page (Admin+) and are meant to be handed upstream so
the image can be patched. By default the game then auto-restarts (`RESTART_ON_CRASH`).

## See also
- [Architecture](architecture.md) · [Configuration reference](configuration.md)
- Source: [`Services/GameSupervisor.cs`](https://github.com/GeekOfWires/tribes2-server/blob/main/panel/Services/GameSupervisor.cs) ·
  [`content/tribes_dual_patcher.py`](https://github.com/GeekOfWires/tribes2-server/blob/main/content/tribes_dual_patcher.py)
- Back to [docs index](README.md)
