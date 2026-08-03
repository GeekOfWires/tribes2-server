# Reverse-engineering notes: recovering V12 engine internals

Working notes for reconstructing the parts of the engine that carry no string literals —
the sim loop, the physics integrator, the ghost manager. Records what worked, what did
**not**, and why, so the failures are not repeated.

Target: the shipped `Tribes2.exe` (4,354,048 bytes, image base `0x00400000`). All addresses
are for that build.

## Method that works

**Linear sweep with resume.** A naive linear disassembly of `.text` stops at the first
undecodable byte and covered only 3,888 instructions — `.text` has data interleaved (jump
tables, padding). Resuming one byte past each stall covers **1,002,556 instructions**.

**Find registrars by call-site shape.** The console registrar is at **`0x00426450`**,
identified as the call target with 388 sites whose preceding pushes carry two string
literals plus a code pointer. Reading each site's arguments back in cdecl order recovers
**372 registrations** (see `Script/Console/EngineFunctionAddresses.md`).

**Corroborate before trusting.** A registration is only accepted when the usage string names
the same method as the registration, so name, address and signature agree from independent
sources.

**Follow callees inward, not callers outward.** See below — this is the correction that
opened up the internals.

## Methods that failed, and why

**Push-clustering (discarded).** Clustering nearby `push imm32` sites cannot distinguish
argument positions. It produced a plausible-looking table that was systematically wrong:
names mispaired with neighbouring usage strings, and namespace arguments mistaken for method
names. Superseded by reading arguments at the call site.

**Navigating outward from registered functions (wrong premise).** The expectation was that
callers of `setTransform` (`0x0058ab20`) and `setScale` (`0x0058ac00`) would bound the
physics integrator. Both have **zero direct call sites** and only two data references — they
are entries in the console registration table. Registered functions are *script-entry
wrappers*; engine code does not call them. The usable direction is the reverse: follow what
they **call**.

**Field-registry heuristic (unresolved).** Searching for an `addField`-shaped registrar
(one string plus small integers) selected `0x00401fe0`, which has 796 call sites but yields
no field names — almost certainly a generic runtime helper matched by coincidence. The
datablock field registry is still unlocated; the physics *parameter names* are known from
the string survey, but their **offsets** are not.

## Findings

| Item | Value |
|---|---|
| Console registrar | `0x00426450`, 388 call sites |
| Registrations recovered | 372 (see the address manifest) |
| Vtable candidates | 440 runs of >=8 consecutive code pointers |
| Virtual dispatch slots | 19,407 total; largest table 386 entries at `0x00787428` |
| Object-model hub | **`0x0055b640`** — shared callee of both transform setters, **147 call sites engine-wide** |
| Transform arg helpers | `0x0054f0f0`, `0x0054f120` (called only by `setTransform`) |

`0x0055b640` is the first internal engine function identified by navigation rather than by a
string literal. Both transform paths converge on it and it is reached from 147 sites, which
is the profile of a central object-state routine rather than a leaf utility.

## Next steps

1. Characterise `0x0055b640` — its callees and the vtable slots it dispatches through.
2. Locate the datablock field registrar properly, to recover field **offsets** and with them
   the physics parameter block layout.
3. Cross-reference the 440 vtable candidates against the class names recovered from the
   string survey, to attach hierarchy to the dispatch surface.
4. Reach the tick path from the object model rather than from strings: it has no literals,
   so it can only be found by dispatch and call-graph structure.
