# The simulation tick loop (recovered from `Tribes2.exe`)

The per-tick execution path, end to end. This is the behaviour a managed server has to
reproduce for a stock client's movement prediction to agree with the server.

Addresses are for the shipped build, image base `0x00400000`.

## The chain

```
GameInterface::processTime(U32 elapsedMs)          0x005c47f0   (vtable 0x799afc, slot 10)
  clamps elapsed to 0x400, applies a timescale
  |
  +-- serverProcess(U32)                           0x005bbbd0
  |     ecx = gServerProcessList (0x009e5ec0)
  |     -> ProcessList::advanceServerTime           0x00602350
  |
  +-- 0x00671ef0        (once per server tick, before advanceObjects — UNIDENTIFIED)
  +-- Net::process                                  0x004392d0
  |
  +-- clientProcess(U32)                            0x005bbb00
        ecx = gClientProcessList (0x009e82f8)
        -> ProcessList::advanceClientTime           0x00602430
```

## Tick rate: 32 ms

`advanceServerTime` computes `target = (mLastTime + delta) & ~0x1F` and steps
`mLastTick += 0x20` per iteration — a **fixed timestep of 32 ms (TickShift 5, 31.25 Hz)**.

Verified directly in the disassembly: `and esi, 0xffffffe0`, `shr dword ptr [ebp-0x1c], 5`,
`add dword ptr [ebx+0x288], 0x20`.

## Client-side passes

`advanceClientTime` runs the same fixed-step tick loop, then makes **two further full passes**
over the process list:

| Pass | Slot | Argument |
|---|---|---|
| interpolate | 55 `interpolateTick(F32)` | `(32 - (mLastTime & 31)) * 0.03125f` — the constant at `0x7a38f4` is 1/32 |
| advance | 56 `advanceTime(F32)` | `delta * 0.001f` (`0x7a38f8`) — milliseconds to seconds |

So `processTick` is fixed-step, while `interpolateTick` receives the fractional remainder and
`advanceTime` receives wall-clock seconds. A re-implementation that feeds all three the same
delta will not match the client.

## `ProcessList::advanceObjects` — `0x00602720`

Uses the link-shuffle idiom: construct a local sentinel, splice the list onto it, then walk
`while ((obj = head.next) != &head)`, re-linking each object before processing it — so objects
may safely add or remove themselves during the pass.

Per object:

- for the connection's control object: `conn->[+0xc4](&moves, &count)`, then
  `obj->[+0xd8](moveList + (i << 6))` for each move, then `conn->[+0xc8](count)`;
- otherwise `obj->[+0xd8](NULL)`, gated on the flag at `+0x264`.

`+0xd8` is vtable slot 54 (`processTick`). The shift of 6 fixes **`Move` at 64 bytes**.

## Player movement

| Address | Size | Role |
|---|---|---|
| `0x005d1d70` | `0x7a0` | `Player::processTick(const Move*)` — calls the ShapeBase base implementation |
| `0x005d7220` | `0x1ab0` (1,686 instructions) | **the Player movement/collision integrator** — calls container routines `0x590cd0`/`0x590d20`, dispatches slot 57 (`getVelocity`) |
| `0x005e8050` | | `ShapeBase::processTick` — per-tick energy/damage accumulation and a mount-list walk, using the tick constant `0.032f` |

`0x005d7220` is the single function that most determines whether client prediction agrees with
the server. It has not yet been decompiled.

## `.exc` is an authoritative function table

The `.exc` section (`0x0072e000`, `0x1be00` bytes) holds **14,241 eight-byte records**
`(function_start_VA, flags_ptr)`, strictly increasing and entirely within `.text`. Verified.

This replaces call-target heuristics with exact function boundaries for the whole binary, and
it is why recursive per-function disassembly is preferable here: a linear sweep is
phase-shifted at several points (`0x55b640` among them) and silently mis-decodes.

## Instance offsets seen along this path

| Offset | Field |
|---|---|
| `+0x4c` | `SceneObject` — `Container::Link` base |
| `+0x9c` | `SceneObject::mObjToWorld` (row-major `MatrixF`; translation at `+0xa8/+0xb8/+0xc8`) |
| `+0x254 / +0x258` | `GameBase::mProcessLink.next / .prev` (`0x5e3110` remove, `0x5e3150` insert-before) |
| `+0x264 / +0x265` | process-tick / advance-time enable flags |
| `+0x2d0 / +0x2d4` | datablock pointer / controlling `GameConnection*` |
| `+0x77c / +0x780` | `ShapeBase` energy / recharge-per-tick |
| `+0x7f0 / +0x7f4` | damage / damage rate |
| `+0x7e0 / +0x7e8` | mount list head / next link |
| `+0x825c` | `GameConnection::mControlObject` |
| ProcessList `+0x288 / +0x28c / +0x290` | `mLastTick` / `mLastTime` / `mLastDelta` |

## Conflicts and gaps — unresolved

- ~~Datablock offset conflict~~ **RESOLVED: both were right.** Each level of the hierarchy keeps
  its own down-cast copy of the datablock pointer, written by its own `onNewDataBlock`:
  `GameBase` at **`+0x248`** (the raw argument), `ShapeBase` at **`+0x2d0`** (cast to
  `ShapeBaseData*`), `Player` at **`+0xa20`** (cast to `PlayerData*`). The casts go through the
  runtime cast helper with type descriptors naming exactly those types, and each level's
  `packUpdate` reads its own copy — `ShapeBase` reads `maxDamage` through `+0x2d0`, `Player`
  reads `maxEnergy` through `+0xa20`. `+0x2d4` is `mControllingClient`.
- ~~Slot 53 unidentified~~ **RESOLVED: `onNewDataBlock(GameBaseData*) -> bool`.** `GameBase`'s
  implementation (`0x5e2a80`, 15 instructions) stores the argument at `+0x248`, returns false if
  null, otherwise calls `setMaskBits(2)` and returns true — and bit 2 is exactly what
  `GameBase::packUpdate` tests to decide whether to serialise the datablock, so the two
  functions corroborate each other. Verified directly.
- **`0x00671ef0`** (size `0xed0`, `ecx = [0x9e8dbc]`) runs once per server tick before
  `advanceObjects`. Unidentified.
- **`GameConnection` slots 49/50/51** (`+0xc4` fetch moves, `+0xc8` clear moves, `+0xcc` a bool
  predicate) are inferred **from usage only** — the GameConnection vtable itself was not located
  by that analysis, though a separate one places it at `0x007a2834`. Worth reconciling.
- Nothing here is verified against a live process.
