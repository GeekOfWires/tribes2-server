# Ghost update wire format (recovered from `Tribes2.exe`)

What `packUpdate` writes and `unpackUpdate` reads, bit for bit. This is the format a managed
server must emit for the stock client to replicate objects correctly.

Addresses are for the shipped build, image base `0x00400000`.

## BitStream primitives

`__thiscall`, `ecx` = the stream.

| Address | Method | Bits |
|---|---|---|
| `0x0043bdb0` | `writeFlag(bool)` | 1 |
| `0x0043bcc0` | `writeBits(count, src)` | n |
| `0x0043bf60` | `writeInt(U32, n)` | n |
| `0x0043bf80` | `writeFloat(F32, n)` = `writeInt(f * ((1<<n)-1), n)` | n |
| `0x0043c000` | `writeSignedFloat(F32, n)` = `writeInt((f+1) * 0.5 * ((1<<n)-1), n)` | n |
| `0x0043c0a0` | `writeSignedInt(S32, n)` = `writeFlag(v<0)` + `writeInt(abs(v), n-1)` | n |
| `0x0043c170` | `writeNormalVector(Point3F, b)` — two `writeSignedFloat` angles | **2b+1** |
| `0x00436ce0` | `writeDataBlockId` — `writeFlag` + `writeInt(id, 11)` | 1+11 |
| `0x005887f0` | `NetConnection::packStringHandleU32` | variable |
| `0x00588ac0` | `NetConnection::writeCompressedPoint` | see below |

Read counterparts: `0x43be10`, `0x43bf10`, `0x43bfd0`, `0x43c060`, `0x43c0f0`, `0x43c260`,
`0x436d10`, `0x588cf0`.

**Ghost ids are 10 bits** (`getGhostIndex` at `0x00584fb0` masks with `0x3ff`).
**Class ids are 7 bits.** **Datablock ids are 11 bits.**

### `writeCompressedPoint` (`0x00588ac0`), called with `scale = 0.01`

Encodes a position as a quantised delta from the connection's scope position when it is close
enough, and falls back to three raw floats when it is not:

```
if scope position valid:  d = p - scopePos ; m = |d| / scale
   level = m < 32768 ? 0 : m < 131072 ? 1 : m < 524288 ? 2 : 3
else level = 3
writeInt(level, 2)
level < 3 -> bits = {16, 18, 20}[level]; writeSignedInt(d.x/scale, bits), then y, then z
level = 3 -> three raw 32-bit floats of the ABSOLUTE position
```

## Pack chain

`Player::packUpdate` (`0x5dae80`) calls `ShapeBase::packUpdate` (`0x5eead0`), which calls
`GameBase::packUpdate` (`0x5e32d0`). **`GameBase` is the root** — `NetObject` and `SceneObject`
contribute no bits. Parent bits are written first. `unpackUpdate` mirrors it exactly
(`0x5db2d0` → `0x5ef0e0` → `0x5e3360`).

## Mask bits

| Bit(s) | Owner | Meaning |
|---|---|---|
| 0 | NetObject | initial update |
| 1 | GameBase | datablock |
| 2 | GameBase | target id / extended info |
| 3 | ShapeBase | damage |
| 4 | Player | no-warp |
| 5 | ShapeBase | mounted |
| 6 / 7 / 8 | ShapeBase | cloak / shield / invincible |
| 9–12 | ShapeBase | sound threads (4) |
| 13–16 | ShapeBase | animation threads (4) |
| 17–24 | ShapeBase | mounted images (8) |
| 25 / 26 / 27 | Player | action anim / move+position / 3-bit field (unknown) |

**This table is self-validating.** ShapeBase's group gate writes `writeFlag(mask & 0x01FFFFE8)`,
and that constant is exactly bits {3} ∪ {5..24} — the union of the blocks it then writes,
excluding bits 0–2 (owned by NetObject/GameBase) and bit 4 (owned by Player). Verified.

## Field layout

**`GameBase::packUpdate`**
1. `writeFlag((mask & 2) && mDataBlock)` → `writeInt(datablockId, 11)`
2. `writeFlag(mask & 4)` → `writeFlag(id != -1)` → `writeInt(id, 9)`

**`ShapeBase::packUpdate`**
- `writeFlag(mask & 0x01FFFFE8)` — **if zero, returns immediately**
- damage: `writeFloat(damage / maxDamage, 6)`, `writeInt(damageState, 2)`, `writeFlag`,
  `writeNormalVector(.., 8)` (17 bits)
- sound threads ×4: `writeFlag` gate, then `writeFlag(play)`, `writeDataBlockId(profile)`
- anim threads ×4: `writeFlag` gate, then `writeInt(seq, 5)`, `writeInt(state, 2)`, 2 flags
- mounted images ×8: `writeFlag` gate, then datablock id, a string handle, 5 flags,
  `writeInt(.., 3)`, plus one extra flag on the initial update
- cloak / shield / invincible blocks, each behind its own flag; shield writes a
  `writeNormalVector(.., 8)` + `writeFloat(energy, 5)`; invincible writes two **raw 32-bit** floats
- mount tail: ghost index as `writeInt(idx, 10)` + `writeInt(node, 5)`

**`Player::packUpdate`**
1. `writeFlag` → `writeInt(.., 3)` (bit 27)
2. action animation: `writeInt(action, 8)`, 3 flags, optional `writeSignedFloat(pos, 6)`
3. `writeFlag` → `writeInt(.., 8)`
4. **`writeFlag(controllingClient == this connection && !initial)` — if true, returns; nothing
   further is written.** The owning client is not sent its own predicted state.
5. move/position block: `writeInt(.., 3)`, optional `writeInt(.., 7)`, 2 flags,
   `writeCompressedPoint(position, 0.01)`, then velocity as
   **`writeInt(min(len*32, 8191), 13)` followed by `writeNormalVector(dir, 10)` (21 bits)** —
   magnitude first, then direction; two `writeSignedFloat(.., 6)` look angles;
   `writeFloat(rot * 1/2pi, 7)`; a sub-object of three optional 16-bit and three 6-bit fields;
   `writeFlag(!(mask & 0x10))`
6. `writeFloat(energy / maxEnergy, 5)`

**Cross-check:** `Player::unpackUpdate` and `ShapeBase::unpackUpdate` were extracted
independently and their read sequences match the write sequences element for element and
width for width.

## Ghost record framing

`NetConnection::writePacket` (`0x587b90`) → `eventWritePacket` (`0x583540`) →
`ghostWritePacket` (`0x583db0`):

**Two corrections from the later packet-prefix work** (see `PacketPrefix.md`): when the
connection is not ghosting the section is **absent entirely — not even the flag**, which is why
client-to-server packets carry no ghost section at all; and `idSize` is clamped to at least 3,
so it is **bounded to 3..10** rather than unbounded.

```
writeFlag(hasGhosts)              // only present when the connection is ghosting at all
writeInt(idSize - 3, 3)           // idSize in 3..10, clamped to a minimum of 3
repeat:
   writeFlag(true)                // another record follows
   writeInt(ghostIndex, idSize)
   writeFlag(isRemove)
   if !isRemove:
      if first time sent: writeInt(classId, 7)
      packUpdate(conn, mask, stream)
writeFlag(false)                  // terminator
```

## Validation status

**Superseded — the prefix has since been recovered and validated.** See `PacketPrefix.md`. The
ghost section is now reachable in real packets: 3,375 of 3,592 fully-prefix-decoded server
packets reach it and parse its header and first record header, with zero hard failures across
both captures.

**What remains unproven is the record *body*.** Traversing a whole ghost record needs the
per-class `packUpdate` for whichever class each ghost is, and a mid-session capture never
carries the class id for ghosts that already existed. So the framing above is confirmed against
real traffic while the field layout below it is still only derived from the disassembly.

## Unidentified

- `ShapeBase` `+0x740` / `byte[+0x774]` inside the shield block; `0x5f6e20(i)` per-image flag.
- The `Player +0x894` sub-object: structure recovered, semantics unknown.
- `GameBase +0x270` — a 9-bit id with a −1 sentinel; **inferred** to be the target id from
  neighbouring event classes, not proven.
- Mask bit 27's meaning.
- `getUpdatePriority`'s full scoring formula (partially decoded only).
- Nothing here has been checked against a live process.
