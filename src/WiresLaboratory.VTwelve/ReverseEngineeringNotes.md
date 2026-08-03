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
| ~~Object-model hub `0x0055b640`~~ | **RETRACTED — it is `sscanf`.** See the correction below. |
| Transform arg helpers | `0x0054f0f0`, `0x0054f120` (called only by `setTransform`) |

**Retraction: `0x0055b640` is `sscanf`.** It was reported here as "the first internal engine
function identified by navigation" and as "a central object-state routine". That was wrong.
It is a variadic forwarder to a `vsscanf` core, and its 147 call sites are console argument
parsers, not object-model code. Verified: 143 of those sites pass a format string, every one
scanf-style, including five distinct `%[...]`/`%*` conversions that exist only in scanf.

The lesson is that *call-site count is not evidence of significance*. `0x0055b640` is central
the way `memcpy` is central. Reaching it from two transform setters looked meaningful only
because both parse string arguments. Identify a function before inferring importance from its
degree.

`0x0054f0f0` and `0x0054f120` are likewise math helpers, not parsers: `AngAxisF::set(const
MatrixF&)` and its inverse, used for the seven-float transform string format.

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

**Independent cross-check, and what it caught.** Across the **122** class pairs where both
child and parent register fields, a child's lowest offset should sit above its parent's
highest offset plus size. It holds for **120**. The **2** failures are
`GuiAviBitmapCtrl` and `ShellTextList` — which are *precisely* the only two classes whose
attribution was marked `inferred-by-elimination` rather than proven from the vtable.

So the invariant is not broken; it is working as a **detector of bad attribution**. Every
vtable-proven mapping satisfies it, and both guessed mappings fail it. Those two attributions
should be treated as wrong until re-derived.

(An earlier revision of this file claimed "119 pairs, zero violations". That came from a check
run with the two low-confidence classes excluded; the committed table yields 122 and 2. The
corrected figures are the ones above.)

The check played no part in building the mapping, and it reproduces the expected lineage on
its own (`PlayerData -> ShapeBaseData -> GameBaseData -> SimDataBlock`).

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
2. (done) The handshake trailers are decoded — see
   `../WiresLaboratory.NextMastery/HandshakeAuthentication.md`. They are bit-packed LSB-first,
   which is why byte-level inspection saw noise. A managed server needs **no secret** to emit a
   valid challenge response: the block is RSA-encrypted under the *client's* public key, which
   the client itself supplies. This unblocks real client attachment.
3. (done) Class hierarchy and virtual dispatch are recovered via RTTI — see
   `Sim/EngineClassLayout.md`. The tick path is located: slots 54/55/56 are
   `processTick`/`interpolateTick`/`advanceTime`, and 17/18/19 are the ghosting entry points.
4. The tick loop is now mapped end to end — see `Sim/EngineTickLoop.md`. What remains is
   behavioural: decompile `0x005d7220` (the 1,686-instruction Player movement/collision
   integrator) and `Player::packUpdate` (`0x005dae80`) for the ghost field layout.
5. Resolve the datablock-offset conflict recorded in `Sim/EngineTickLoop.md` (`+0x248` from
   slot 53 versus `+0x2d0` along the tick path) before building against either.
