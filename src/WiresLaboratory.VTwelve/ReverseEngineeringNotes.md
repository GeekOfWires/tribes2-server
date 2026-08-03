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

**Field-registry heuristic (failed, then solved a different way).** Searching for an
`addField`-shaped registrar by call-site *shape* selected `0x00401fe0` — 796 call sites, zero
field names, a generic runtime helper matched by coincidence. Shape alone is too weak a
signal. What worked instead was **string cross-reference**: take the virtual addresses of
field-name strings already known to exist, find every `push` of those addresses, and see
where they converge. See "Datablock field registry" below.

## Findings

| Item | Value |
|---|---|
| Console registrar | `0x00426450`, 388 call sites |
| Registrations recovered | 372 (see the address manifest) |
| Vtables (RTTI-backed) | 774, from descriptors at `vtable-8` |
| ~~Virtual dispatch slots 19,407~~ | **Corrected** — that counted 209 non-vtable runs. `0x00787428` (386 entries) is a jump table with no descriptor. See `Sim/EngineClassLayout.md`. |
| Object-model hub | **`0x0055b640`** — shared callee of both transform setters, **147 call sites engine-wide** |
| Transform arg helpers | `0x0054f0f0`, `0x0054f120` (called only by `setTransform`) |

`0x0055b640` is the first internal engine function identified by navigation rather than by a
string literal. Both transform paths converge on it and it is reached from 147 sites, which
is the profile of a central object-state routine rather than a leaf utility.

## Datablock field registry (resolved)

**Registrar: `0x00423F20`**, cdecl with five arguments and `add esp, 0x14` at every site:

```
addField(const char *name, U32 typeCode, U32 offset, U32 elementCount, EnumTable *table)
```

Found by string cross-reference rather than by call-site shape: locate the addresses of
known field-name strings, find every `push` of them, and observe that all converge on one
target. Verified independently — 1,416 call sites, and 400 of 400 sampled resolve a real
nul-terminated string as the first argument, which a generic helper could not.

The structural lock that makes this certain: the fifth argument is non-NULL for exactly 19
fields, and **every one of those has type code 9** — the `EnumTable *` slot is populated if
and only if the field is the enum type. Nothing coincidental produces that correspondence.

**1,415 fields across 156 classes** are recorded in `Sim/RecoveredDatablockFields.tsv`
(name, type code, size, offset, element count, owning class, parent class, registrar).

Class attribution is not guessed. Each class-rep stores its name, its vtable slot 2 is
`init()`, and `init()` calls that class's `initPersistFields` — so class to registrar falls
out of the vtable. The parent link comes from `init()` calling the parent's class-rep
accessor, which is what disambiguates registrars shared by inheritance.

**Independent cross-check:** across the 119 class pairs where both child and parent register
fields, every child's lowest offset sits above its parent's highest offset plus size —
**zero violations**. That check played no part in building the mapping, so it is genuine
corroboration, and it also reproduces the expected lineage on its own
(`PlayerData -> ShapeBaseData -> GameBaseData -> SimDataBlock`).

Type codes with sizes derived from consecutive offset deltas: 1=S32(4), 3=bool(1), 5=F32(4),
7=char*(4), 8=StringTable(4), 9=enum(4), 11=ColorI(4), 12=ColorF(16), 14=Point2I(8),
16=Point3F(12), 18=RectI(16), plus a family of datablock/profile pointers. The type *names*
are labels applied here, not the engine's own.

Known gaps: the 17 `EnumTable` pointers all target `.bss`, so they are built at runtime and
their string-to-value mappings cannot be read statically; one site in `TSShapeConstructor`
passes its arguments in registers and is not statically recoverable; and nothing has been
checked against a live process.

## Next steps

1. Characterise `0x0055b640` — its callees and the vtable slots it dispatches through.
2. Decode the two opaque handshake trailers (684 bytes client-side, 130 bytes server-side).
   The server-authored one blocks everything: until it is understood, a managed server cannot
   emit a challenge response the stock client will accept.
3. (done) Class hierarchy and virtual dispatch are recovered via RTTI — see
   `Sim/EngineClassLayout.md`. The tick path is located: slots 54/55/56 are
   `processTick`/`interpolateTick`/`advanceTime`, and 17/18/19 are the ghosting entry points.
4. Disassemble `Player::processTick` (`0x005d1d70`) and `Player::packUpdate` (`0x005dae80`)
   to recover the movement integrator and the ghost field layout — the two remaining
   behavioural unknowns.
