# Engine class layout and virtual dispatch (recovered from `Tribes2.exe`)

The object model, its inheritance, and the virtual slots the simulation and ghosting run
through. This is the part of the engine that carries **no string literals** — it could not be
reached by the string surveys and was recovered structurally.

Addresses are for the shipped build (4,354,048 bytes, image base `0x00400000`).

## How this was recovered

The compiler emits an **RTTI descriptor immediately before every vtable**, at a fixed layout:

```
vtable-8 : descriptor*   -> { const char *name ; BaseEntry *bases }
vtable-4 : offset-to-top  (0 for a primary base, negative for a secondary)
vtable+0 : slot 0 ...
```

`BaseEntry[]` is a zero-terminated array of `{descriptor*, S32 offset}` giving the **full
transitive ancestor set** with each base's byte offset inside the derived object. So one
structure yields name, vtable *and* inheritance together.

Recovered: **3,796 descriptors, 774 vtables, 767 distinct class names**, 231 of them in the
`SimObject` family. Corroborated by scanning `.text` for constructor stores of the form
`mov [reg+disp], imm32` where the immediate is a recovered vtable: **774 of 774** vtables have
one (3,715 sites), with no false positives.

Verified again here, independently of the search that produced it: for nine sampled classes
the descriptor at `vtable-8` resolves to exactly the expected class name, and the sim/ghost
slots hold genuinely different function addresses per class rather than a repeated default.

**A prior claim is corrected by this.** An earlier survey counted "440 runs of >=8 consecutive
code pointers, 19,407 dispatch slots". That was an overcount: **209 of those runs have no RTTI
descriptor and are not vtables**. The largest, `0x00787428` (386 entries), has only 267 distinct
targets and zero constructor stores — it is a jump table. The real virtual-dispatch surface is
the **774 descriptor-backed tables**.

## Class to vtable

| Class | vtable | slots | ClassRep (`.bss`) |
|---|---|---|---|
| `ConsoleObject` | `0x0074d524` | 2 | |
| `SimObject` | `0x00755c60` | 17 | `0x009ecd74` |
| `SimSet` | `0x00755b50` | 21 | |
| `SimGroup` | `0x00755bac` | 21 | |
| `NetObject` | `0x00792058` | 22 | `0x009eb7a0` |
| `SceneObject` | `0x00793094` | 53 | `0x009eb71c` |
| `GameBase` | `0x0079dfac` | 67 | `0x009eb2d0` |
| `ShapeBase` | `0x007a0774` | 125 | `0x009eb278` |
| `Player` | `0x0079cad4` | 125 | `0x009eb354` |
| `Vehicle` | `0x007a5788` | 129 | |
| `WheeledVehicle` | `0x007a6d20` | 129 | |
| `FlyingVehicle` | `0x007a629c` | 129 | |
| `HoverVehicle` | `0x007a7924` | 129 | |
| `Projectile` | `0x007acc00` | 69 | |
| `Item` | `0x007a4688` | 125 | |
| `StaticShape` | `0x007a3c98` | 125 | |
| `Camera` | `0x0079b010` | 125 | |
| `TSStatic` | `0x007bf47c` | 53 | |
| `Trigger` | `0x007a88f8` | 67 | |
| `InteriorInstance` | `0x007801d4` | 53 | |
| `TerrainBlock` | `0x007951dc` | 53 | |
| `NetConnection` | `0x007927ac` (39 slots, at `this+0xA0`) | | |
| `GameConnection` | `0x007a2834` (42 slots, at `this+0xA0`) | | |

`slot 0` is `getClassRep()`, a three-instruction stub returning the address of the static
class-rep — which is where the `.bss` addresses above come from.

## Hierarchy

```
ConsoleObject
└─ SimObject
   ├─ SimSet ─ SimGroup ─ GuiControl (35 subclasses) / Path / SimDataBlockGroup
   │              └─ NetConnection ─ GameConnection ─ AIConnection
   └─ NetObject
      └─ SceneObject                     (Sky, Sun, Marker, InteriorInstance,
         │                                TerrainBlock, TSStatic, PhysicalZone, …)
         └─ GameBase                     (Debris, Explosion, Lightning, Precipitation,
            │                             ParticleEmitter, Splash, Shockwave, Trigger, …)
            ├─ Projectile ─ {ELF, Repair, ShockLance, Sniper, Target}Projectile
            └─ ShapeBase ─ Player | StaticShape | Camera | Item | MissionMarker
                         └─ Vehicle ─ {Wheeled, Flying, Hover}Vehicle
```

Datablocks form a parallel tree: `SimDataBlock -> GameBaseData -> ShapeBaseData ->
{Player,Vehicle,Camera,StaticShape,MissionMarker}Data`.

**Multiple inheritance matters for two classes:**

- `SceneObject : NetObject, Container::Link` — the `Container::Link` base sits at **+76** and
  contributes no virtuals.
- `NetConnection : ConnectionProtocol, SimGroup` — `sizeof(ConnectionProtocol)` is **160
  (0xA0)** and the `SimGroup` subobject lives at **+0xA0**. `GameConnection`'s 42-slot table is
  built from adjustor thunks (`add ecx, -0xA0; jmp real`), which pins the layout.

  **Correction.** An earlier revision of this file said all SimObject-lineage dispatch on a
  connection goes through the table at `this+0xA0`. That is misleading as a *decoding* rule:
  call sites index from the **offset-0 vptr** with byte displacements that run past the 8-slot
  `ConnectionProtocol` sub-table into the adjacent one. For example `writeCompressedPoint` is
  `[vptr0 + 0xbc]` and `writePacket` is `[vptr0 + 0x94]`, and both `NetConnection` (`0x792784`)
  and `GameConnection` (`0x7a280c`) resolve `+0xbc` to the same `0x588ac0`. Treating the
  `+0xA0` table's own slot numbering as the dispatch index gives wrong answers.

Slot prefixes are strictly compatible throughout: every child table is its parent's with
overrides plus appended slots, with zero violations across the 24 relationships checked.

## Virtual slots

The simulation and ghosting entry points. Confidence is recorded per row because several were
identified from code shape rather than from a literal.

| Slot | Method | Confidence |
|---|---|---|
| 0 | `getClassRep()` | confirmed |
| 1 | scalar deleting destructor | confirmed |
| 2 | `processArguments(argc, argv)` | confirmed |
| 3 | `onAdd()` — sets flag bit 3 at `this+0x18` | confirmed |
| 4 | `onRemove()` — clears it | confirmed |
| 15 | `write(Stream&, tabStop, flags)` | inferred |
| **17** | **`getUpdatePriority(CameraScopeQuery*, U32, S32) -> F32`** | **confirmed** |
| **18** | **`packUpdate(NetConnection*, U32 mask, BitStream*) -> U32`** | **confirmed** |
| **19** | **`unpackUpdate(NetConnection*, BitStream*)`** | **confirmed** |
| 20 | camera-scope / ghost hook | inferred |
| 22 / 23 | `disableCollision()` / `enableCollision()` (`this+0x22c`) | confirmed |
| **29** | **`setTransform(const MatrixF&)`** | **confirmed** |
| 32 / 33 / 35 | `buildConvex` / `buildPolyList` / `castRay` | inferred |
| 39 / 40 | `renderObject` / `prepRenderImage` | inferred |
| 42 / 43 | `onSceneAdd(SceneGraph*)` / `onSceneRemove()` (`this+0x230`) | inferred |
| 44–49 | pure-virtual stubs | confirmed |
| **53** | **`onNewDataBlock(GameBaseData*) -> bool`** — stores its argument, then `setMaskBits(2)`; each level keeps its own down-cast copy (`GameBase +0x248`, `ShapeBase +0x2d0`, `Player +0xa20`) | **confirmed** |
| **54** | **`processTick(const Move*)`** — the simulation tick | **confirmed** |
| **55** | **`interpolateTick(F32 delta)`** | **confirmed** |
| **56** | **`advanceTime(F32 dt)`** | **confirmed** |
| **57** | **`getVelocity(Point3F& out)`** | **confirmed** |
| 65 / 66 | `writePacketData` / `readPacketData` | inferred |

Slots 54 and 55 corroborate each other: 54 zeroes the field at `this+0x268` and 55 stores its
float argument into that same field, which is the interpolation-delta relationship those two
methods have by definition.

## Instance field offsets recovered as a side effect

| Field | Offset |
|---|---|
| `SimObject` flags (bit 3 = added) | `+0x18` |
| `SceneObject::mCollisionCount` | `+0x22c` |
| `SceneObject::mSceneManager` | `+0x230` |
| `GameBase::mDataBlock` | `+0x248` |
| `GameBase` tick flag | `+0x265` |
| `GameBase` interpolation delta | `+0x268` |
| `Player::mVelocity` | `+0x958` |
| `Item::mVelocity` | `+0x8b0` |

## Not determined

- **Slot 21** (NetObject's fifth virtual): no arguments, returns true. Not enough to name.
- **`SimObject` slots 5–14 and 16**: mostly empty `ret` stubs that COMDAT-folding shares with
  unrelated hierarchies, so identity cannot be pinned from code shape.
- **`ShapeBase`'s own slots 67–124** and **`NetConnection`'s own slots 21–38** are unnamed,
  beyond `Projectile.68 = calculateImpact`.
- Nothing here has been checked against a live process.

## Method note

A global histogram of `call [reg+disp]` displacements is **useless** for locating sim-loop
slots: the `GuiControl` hierarchy (35+ subclasses) occupies the same displacement range and
dominates the counts. Dispatch sites have to be attributed to their owning class first.
