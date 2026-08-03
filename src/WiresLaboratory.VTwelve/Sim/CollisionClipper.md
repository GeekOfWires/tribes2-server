# `ExtrudedPolyList` — the swept-volume collision clipper

The class that produces the contact time, contact normal and max height every branch of
`Player::updatePos` depends on. Recovering it was the stated barrier to exact client/server
movement agreement.

Addresses are for the shipped build, image base `0x00400000`.

## Identity

Located via its RTTI descriptor rather than by inference: descriptor `0x0074f0cc` →
**vtable `0x0074f0dc`**, 14 slots, single base `AbstractPolyList` at offset 0, `sizeof` `0x1a4`.

Corroborated independently: the immediate `0x0074f0dc` occurs in `.text` exactly twice — the
constructor `0x0041f100` and the destructor `0x0041f3a0`. Nothing else installs that vtable.

Key functions: `extrude` `0x0041f410`, `end` `0x0041fda0`, and the private clipper
`clipFace` `0x004200d0` called from `end`.

## Contact time

```
face.maxDistance = face.plane.n · extrudeVector           // computed once, in extrude
minDist          = max(0, min over surviving verts of (face.plane.n · v + face.plane.d))
reject if  face.maxDistance + 0.01 <= minDist
face.t           = minDist / face.maxDistance
```

Verified at byte level: both divides are `D8 F1` (`ST0 = ST0 / ST1`), so neither is a reversed
divide — a detail worth checking explicitly, because getting it backwards produces plausible
values rather than obvious nonsense.

## The finding most likely to cause a silent desync

**`adjustCollisionTime` (`0x0041f8c0`) is never called by `Player::updatePos`.**

That function subtracts a per-face 1 cm slop (`0.01 / maxDistance`) and clamps to `[0,1]`. It
looks exactly like something a movement integrator ought to apply, and applying it is wrong
here. Its only two call sites in the whole image are elsewhere, and the `mTimeSlop` fields of
both static instances (`0x0083fe14`, `0x0083ffc8`) have **zero references** in `.text`, so it is
not inlined into `updatePos` either. `updatePos` reads `colList->t` raw.

A re-implementation that "sensibly" applies the slop will disagree with the client.

## Tie-breaking

Fully pinned, and it matters for determinism:

- faces are considered in `Polyhedron::planeList` order;
- selection is by largest `faceDot` using a **strict** `>`, so ties go to the lowest index;
- `cl->t` latches and is **not** lowered by a later face inside the ±1e-4 band;
- normal selection is two-pass — polygon normal, then inverted face normal.

## Tolerances

| Value | Address |
|---|---|
| `0.01f` | `0x0074f05c`, `0x0074f078` |
| `1e-4f` | `0x0074f060` |
| `0.0f` | `0x0074f064` |
| `1.0f` | `0x0074f068` |
| `±1e30f` | `0x0074f07c`, `0x0074f080` |
| `1e-4` (double) | `0x0074f088` |

There is also a 32-entry `1 << k` bit table at `0x0074efd8` which **returns 0 for a plane index
of 32 or above** — a genuine edge case for axis-aligned motion, recorded as found rather than
smoothed over.

## Why bit-exactness still cannot be claimed

**The x87 precision-control setting is not determined**, and it decides the answer. The
plane-distance dot products accumulate at FPU register width and round to `float` only when
stored, so the same source expression yields different low bits under 53-bit versus 64-bit
precision control. Until that is pinned, this reproduces the *algorithm* faithfully but cannot
be asserted bit-identical.

The division itself is safe — both operands are loaded from `float` memory.

## Not determined

- Which class owns the one-argument `buildPolyList` at `[vptr+0x1c]`.
- The role of the second static polylist (`0x0083fe50`) and the object flag `0x00800000` that
  selects it — deliberately not guessed.
- The identity of the two functions that *do* call `adjustCollisionTime`.
- `Collision+0x28`.
- Nothing verified against a live process.
