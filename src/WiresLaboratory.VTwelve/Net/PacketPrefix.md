# In-session packet prefix (recovered and validated against captured traffic)

Everything that precedes the ghost section in a sequenced packet. This is what turned the
ghost format from *derived* into *tested against real packets*.

Addresses are for the shipped build, image base `0x00400000`.

## Connection header — `buildSendPacketHeader` `0x0043d2d0`, read at `0x0043d4d0`

| Bits | Field |
|---|---|
| 1 | constant `1` — sequenced packet; `0` marks out-of-band control |
| 1 | connect-sequence parity; the receiver drops the packet on mismatch |
| 9 | send sequence |
| 9 | ack sequence |
| 2 | packet type — **receiver rejects a value above 2** |
| 3 | ack byte count — **receiver rejects a value above 4** |
| 8 × count | ack mask |

Only packet type 0 carries a body.

This **extends** the earlier `PacketHeader` model, which stopped after the ack sequence. Its
"two flag bits" are the sequenced-packet marker plus the connect-sequence parity, and five more
fixed bits and a variable mask follow. Bit 0 is also exactly what `ControlPacket.IsControl`
tests, so that classification and this layout agree by construction rather than by luck.

## `GameConnection::writePacket` `0x005fc4e0`

Branches on whether the connection is the server's. Note `NetConnection::writePacket`
(`0x00587b90`) writes nothing itself — it is a thunk calling the event and ghost writers, and
`GameConnection` calls it **last**.

**Client to server:** a flag; 32 raw bits of control-object checksum; the move block; then an
optional 8-bit field.

The move block (`writeMoves` `0x00601e90`, `Move::pack` `0x00601740`) is a 32-bit first-move
sequence, a 5-bit count (at most 30), then per move: three optional 16-bit look angles, three
6-bit axes, a free-look flag and six trigger flags — 28 to 76 bits each.

**Server to client:** a 32-bit move acknowledgement; two optional 7-bit float pairs; an optional
camera block; two datablock flags; the control-object block; a 16-entry delta vector; an optional
8-bit tail.

The control-object block matters beyond itself: when it is not a resend it carries **96 raw bits
of position as three IEEE floats**, and that is what sets the connection's scope position — the
origin `writeCompressedPoint` deltas every ghosted position against.

## Event section — `eventWritePacket` `0x00583540`, read at `0x005836e0`

An unordered run, then an ordered run:

```
while flag: int(classId - 255, 6); event.pack()      // unordered
flag = 0
next = 127
while flag:                                          // ordered
   next = (next + 1) & 0x7f
   flag(seq == next); if not, int(seq, 7)
   int(classId - 255, 6); event.pack()
flag = 0
```

The read side confirms both the class-id bias and the 7-bit sequence wrap.

## Two corrections to `GhostProtocol.md`

1. **When the connection is not ghosting the section is absent entirely — not even the flag.**
   That is why client-to-server packets carry no ghost section at all.
2. **`idSize` is clamped to at least 3 and written as `idSize - 3` in 3 bits, so it is bounded
   to 3..10** — not unbounded as previously written.

The rest of the framing in that document is correct as written.

## Validation against the captures

| Capture / direction | Packets | Result |
|---|---|---|
| `t2-client-session`, client→server | 4,011 | **3,962 (98.8%) decoded to the exact byte boundary**; 45 stopped inside an event body; 4 were header-only by design. **0 failures** |
| `t2-connect-sessions`, client→server | 2,556 | 2,505 exact; 44 event-body stops; 7 non-data. **0 failures** |
| `t2-client-session`, server→client | 3,703 | 3,592 decoded the full prefix; 3,375 reached the ghost section and read its header and first record header; 21 decoded fully to the tail. **0 hard failures** |
| `t2-connect-sessions`, server→client | 1,984 | 1,961 prefix-decoded; 34 fully to tail. **0 hard failures** |

## Why this is not a lucky fit

A "did it end near the tail" test has several bits of slack, so the result was checked against
deliberate perturbations rather than trusted:

- **Leftover bits are tightly quantised.** With the recovered layout the client-to-server
  leftover takes exactly two values (`{0, 4}`) with **zero overruns** — packet length is a
  deterministic function of content. Every perturbation smears it: one stray bit gives 35
  distinct values and 638 overruns; 5-bit move axes give 30 values and 1,148 overruns; seven
  triggers give 3,399 overruns.
- **The two flows corroborate each other.** The client's 32-bit first-move sequence runs
  105 → 4188 monotonically over 4,007 packets, and the server's 32-bit move acknowledgement runs
  105 → 4188 monotonically over 3,699 packets **in the opposite direction**, differing by −1 in
  85% of samples and within ±4 in 99.8%. Two independently written fields in opposite flows
  cannot agree like that under a misalignment.
- **The 96-bit scope position decodes as a real trajectory.** 10,383 floats, **100%** inside a
  plausible map volume. Shifted by one bit, 58.6% plausible over only 198 surviving floats; by
  two bits, **0%**, with values spanning ±10²⁹.
- **Header fields respect exactly the bounds the receiver enforces.** Across 12,256 packets the
  packet type was only ever 0–2 and the ack byte count only 0–4 — precisely the two reject
  conditions in the read function.

## Still blocking a full end-to-end ghost decode

Per-class `packUpdate` bodies. The ghost section is now reachable and its header and record
headers parse, but traversing a whole record needs the update body for whichever class each
ghost is — and a mid-session capture never carries the class id for ghosts that already existed.
**So the ghost record layout is reachable and its framing confirmed, but not yet proven
end-to-end.** Also undecoded: per-class event bodies (45 of 4,011 client packets) and the
control-object `writePacketData` (107 of 3,703 server packets).

Statically confirmed but never exercised in either capture, so untested on the wire: the server
camera block, the second server flag pair, and the server tail field. Barely exercised: the rate
blocks and the delta-vector entries.
