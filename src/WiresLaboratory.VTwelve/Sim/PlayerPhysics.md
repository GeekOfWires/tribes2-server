# Player movement and collision (recovered from `Tribes2.exe`)

The movement model a managed server must reproduce for the stock client's local prediction to
agree with it. Disagreement here is what players experience as rubber-banding.

Addresses are for the shipped build, image base `0x00400000`.

## The work is split across two functions

An earlier note in this repo identified `0x005d7220` as "the Player movement/collision
integrator". That is half right and the naming was misleading:

| Address | Size | What it actually is |
|---|---|---|
| `0x005d2d60` | `0x1e50` | **`Player::updateMove`** — the force model. Turns input into `mVelocity`. |
| `0x005d7220` | `0x1ab0` | **`Player::updatePos(F32 travelTime) -> bool`** — position integration and collision. Takes a **float**, not a `Move*`. |

`Player::processTick` calls `updateMove` first, then `updatePos`. Both had to be recovered;
neither alone is the movement model.

## Timestep: 1/32 s exactly

The movement code uses the constant at `0x0079c140` = **`0.03125f`**, verified. This is exact
binary 1/32, not a decimal approximation.

**Correction:** an earlier note in this repo cited `0.032f` as "the tick constant". That value
occurs exactly once on the whole tick path, in an unrelated `ShapeBase` timer accumulator. Using
`0.032` in the movement maths introduces a 2.4% per-tick error against the client — small enough
to look like a tuning discrepancy and large enough to desynchronise prediction.

## Integration scheme

**Semi-implicit Euler, split across the two functions**: `updateMove` finishes `mVelocity`, then
`updatePos` integrates position from the *already updated* velocity.

At the integration site the update is `mVelocity += acc` — with **no mass division and no `dt`**.
Every contributing term is pre-scaled by the caller. The one exception is the **jump**, which is
an impulse: `jumpForce / mMass` with no `TickSec` factor.

Order of operations after the velocity update — this order matters:

1. `v += acc`
2. horizontal resist
3. vertical resist
4. buoyancy
5. drag

`mDrag` is **zero out of water**, so on land all speed limiting comes from the
`horizResistSpeed`/`horizResistFactor`/`upResistSpeed`/`upResistFactor` model, not from drag.

## Gravity

`-20.0f`, stored at `0x007a1a20`. Verified.

It is a **mutable global, not a compile-time constant**, so a faithful server must read it rather
than hard-code the value.

## Collision response

```
v += n * (-(v . n) + 0.01f)
```

No restitution and no friction coefficient. The `0.01` term is the separation bias.

- The retry loop is **capped at 5 iterations**. On exhaustion the entire tick is **rolled back**:
  position restored and velocity zeroed.
- The 1 cm back-off is **not time-symmetric**: position is pulled back by
  `min(0.01 / speed, moveTime)`, but that time is **not credited back** to the budget. This is
  easy to implement "correctly" and thereby get wrong.

## Surface angles are precomputed, not read directly

Neither movement function reads `runSurfaceAngle` or `jumpSurfaceAngle`. `PlayerData::preload`
(`0x005cddf0`) converts them once into cosines stored in **derived fields at `PlayerData+0xcd8`
and `+0xcdc`**, which do not appear in the recovered `addField` registry because they are not
registered fields. `Player::findContact` (`0x005d8cd0`) then tests `contactNormal.z > cos`.

A re-implementation that stores the angles in degrees and compares angles will not match; the
engine compares cosines.

## Corrections to earlier project findings

- **`0x590cd0` / `0x590d20` are `Vector<T>` constructors**, not container/collision routines as
  previously recorded. Verified: they zero three fields (`+0`, `+4`, `+8`) and grow on a capacity
  check. `0x69a6b0` / `0x69a940` / `0x69aa60` are `CameraShake` — cosmetic only.
- **The datablock-pointer offsets are all real**, independently agreeing with the ghost-format
  analysis: `+0x248` (GameBase), `+0x2d0` (`ShapeBaseData*`), `+0xa20` (`PlayerData*`). Movement
  code uses `+0xa20`.
- The binary was built with **Metrowerks CodeWarrior**, which explains CRT shapes that do not
  match MSVC expectations (`0x6f73d0` is `__register_global_object`, not `atexit`).

## What the implementation had to guess

`Sim/Physics/` implements this model, but three things it needs are **not** in this document
because they were never recovered from the disassembly. They are inferred, and they are
load-bearing:

- **The resist curve's shape.** This document establishes that the horizontal/vertical resist
  stage exists, where it sits in the order, and that it is the *only* speed limiter on land —
  but not its formula. The implementation uses a hard clamp at `*MaxSpeed` with a geometric
  decay above `*ResistSpeed`, which is conventional for this lineage and **unverified**. Since
  nothing else bounds ground speed, an error here is an error in every player's top speed.
- **The jump speed clamp.** `minJumpSpeed`/`maxJumpSpeed` exist as `PlayerData` fields but play
  no part in the recovered jump description. A post-impulse clamp is inferred from the names.
- **Buoyancy.** Only its existence and position in the order are recovered. It is present but
  numerically inert.

Recovering `Player::updateMove`'s resist arithmetic is therefore the highest-value remaining
physics work, ahead of anything in the list below.

## Gaps

- ~~`ExtrudedPolyList`'s clipper was not analysed~~ **— now recovered, see
  `CollisionClipper.md`.** The contact-time formula, tie-breaking and every tolerance are
  documented there. Two things carried back here: `Player::updatePos` reads `colList->t`
  **raw** and must *not* apply `adjustCollisionTime`'s 1 cm slop (that function is never called
  on this path), and bit-exactness still cannot be claimed because the x87 precision-control
  setting is undetermined.
- The `recoverDelay` scaling after a hard landing — FPU stack scheduling unresolved.
- Animation-driven ground displacement (`0x5d4e90`): a server that does not run the shape's
  animation tree cannot reproduce it.
- **Observed quirk, recorded as found:** the ground-displacement zero-tests check only `.x` and
  `.y`, so a pure-Z displacement is silently dropped. Confirmed twice in the disassembly. A
  "corrected" re-implementation would diverge from the client.
- Nothing verified against a live process.
