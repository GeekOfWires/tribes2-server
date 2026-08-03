# Parallel execution plan

A dispatch-ready plan for the remaining work. Each task is scoped so it can be handed to an
agent without further design, and so that concurrently-running agents cannot collide.

## Model assignment

| Model | Used for | Why |
|---|---|---|
| **Opus 5** | Reverse engineering | Long analytical chains over disassembly, where a plausible-but-wrong answer is expensive and hard to detect. Every retraction this project has issued came from a shortcut in this class of work. |
| **Sonnet 5** | Engineering from a recovered spec | The hard thinking is already in the document; the task is faithful translation with tests. Fast and accurate enough when the spec is written down. |
| **Haiku 4.5** | Mechanical refactoring | Consolidation, renames, doc passes, dead-code removal — work verified by the build rather than by judgement. |

## Rules every agent gets

These are not boilerplate; each one exists because its absence cost this project time.

1. **Clean-room from the binary.** No leaked engine source. Torque 3D (MIT) is an architectural
   reference only.
2. **Report negative results.** Nine findings have been retracted so far. An incomplete answer
   beats a confident wrong one; "not determined" is always an acceptable deliverable.
3. **Separate CONFIRMED from INFERRED** in every report, per claim.
4. **Never guess a wire format or a formula into code.** Leave it unimplemented and flag it. The
   only guessed formula currently in the tree — the resist curve — is Wave 1's first task
   precisely because it is guessed.
5. **Do not commit.** The driver verifies and commits. Agents that commit concurrently corrupt
   the index and hide their own failures.
6. **Stay inside your assigned directory.** See the allocation table.
7. **Prefer cross-corroboration over self-consistency.** Two independent sources agreeing is the
   standard; a round-trip that only agrees with itself proves nothing about the client.

## Directory allocation

Concurrency safety depends on this. No two simultaneously-running agents share a directory.

| Task | Owns |
|---|---|
| Resist arithmetic (RE) | *(analysis only, no repo writes)* |
| Query protocol (RE) | *(analysis only)* |
| RSA handshake (ENG) | `src/WiresLaboratory.NextMastery/` |
| Clipper (ENG) | `src/WiresLaboratory.VTwelve/Sim/Collision/` |
| Packet prefix codec (ENG) | `src/WiresLaboratory.VTwelve/Net/Prefix/` |
| Query responder (ENG) | `src/WiresLaboratory.VTwelve/Net/Query/` |
| Scoping/datablock transmit (ENG) | `src/WiresLaboratory.VTwelve/Net/Scope/` |
| DSO VM (ENG) | `src/WiresLaboratory.VTwelve/Script/Vm/` |
| Integration | `src/WiresLaboratory.VTwelve.WilderzoneServer/` *(driver only)* |

Self-check classes go in `tools/WiresLaboratory.VTwelve.Tools/` as **new files**, with at most
one added dispatch line in `Program.cs`.

---

## Wave 1 — five agents in parallel, no dependencies

### 1A · Resist arithmetic — **Opus, RE**
Recover the horizontal/vertical resist formula from `Player::updateMove` (`0x005d2d60`).
**This is the highest-priority task in the whole plan:** it is the only guessed formula live in
the tree, and it is the sole speed limiter on land, so an error is an error in every player's
top speed. Also settle `minJumpSpeed`/`maxJumpSpeed` and the buoyancy term.
*Gate:* the recovered formula reproduces the implementation's steady state, or explains why it
differs.

### 1B · Query protocol — **Opus, RE**
Decode the `0x0e`/`0x10` and `0x12`/`0x14` payloads. Small, self-contained, and it is what makes
a server appear in the browser. Fixtures contain real exchanges.
*Gate:* decode every query exchange in both captures with no leftover bytes.

### 1C · RSA handshake — **Sonnet, ENG**
Implement the challenge response from `HandshakeAuthentication.md`: parse the client
certificate, extract `(e, n)`, generate a server challenge, emit `flag + 9-bit length + RSA
block`, bit-packed LSB-first. Certificate signature verification is an operator policy — make it
opt-in, not mandatory.
*Gate:* against the captured `0x1a`, produce a `0x1e` structurally identical to the captured
one (length, framing, block size).

### 1D · Collision clipper — **Sonnet, ENG**
Implement `CollisionClipper.md`: the contact-time formula, the tie-breaking rules, every
tolerance. **Do not apply `adjustCollisionTime`'s slop** — the engine does not, on this path.
Replace the flat-plane stub behind the existing `ICollisionSurface`.
*Gate:* the documented tie-break cases resolve as specified; the physics self-check still passes.

### 1E · Packet prefix codec + Move block — **Sonnet, ENG**
Implement `PacketPrefix.md`: the full connection header, the `GameConnection` prefix both
directions, the move block, and the event section framing. Replace `Move`'s placeholder with the
recovered layout.
*Gate:* re-decode the fixtures through the C# codec and match the Python results — 3,962/4,011
client packets to the exact byte boundary, zero failures.

---

## Wave 2 — after Wave 1

### 2A · Scoping and datablock transmission — **Opus, RE**
How the server decides which objects a client is ghosted, and how datablocks first reach a
client. Currently a hole between "ghost format known" and "ghosting works".

### 2B · Query responder — **Sonnet, ENG** *(needs 1B)*
Serve real query replies. First externally-observable milestone: **the server appears in the
in-game browser.**

### 2C · Wire the prefix into the host — **driver** *(needs 1E)*
Replace the partial header parse in `ServerHost`, and connect the ghost codec behind it.

### 2D · Live connection attempt — **driver + you** *(needs 1C)*
Point a real client at Wilderzone. First end-to-end proof, and the first honest test of whether
the handshake work is right.

### 2E · Consolidate BitStream extensions — **Haiku, refactor**
`Net/Ghost/` and `Net/Prefix/` will both carry quantised-float and signed-int helpers. Merge into
one shared location, delete duplicates.
*Gate:* build clean, all self-checks still pass.

---

## Wave 3 — larger, independent efforts

### 3A · Per-class `packUpdate` — **Opus, RE** *(needs a capture, see below)*
Vehicle, Item, Projectile and the rest. Closes the last ghost gap.

### 3B · DSO opcode table — **Opus, RE**
Derive the v174 instruction set empirically using the engine as an oracle: emit targeted
scripts, let the engine compile them, read back which opcodes appear. Independent of everything
else; can start at any time.

### 3C · DSO bytecode VM — **Sonnet, ENG** *(needs 3B)*
Execute compiled code blocks. Validate by diffing console output against the reference server.

### 3D · Console object system — **Sonnet, ENG** *(needs 3C)*
SimObject, namespaces, packages, schedules — the surface the 304 recovered signatures attach to.

### 3E · Console function coverage — **Sonnet, ENG**, ongoing
11 of 304 implemented. Parallelisable in batches once 3D exists.

---

## Needs you, not an agent

- **A fresh-connect capture with a client joining.** Mid-session captures never carry the class
  id for ghosts that already existed, so 3A is blocked without one. ~60 seconds of capture from
  before you press connect.
- **A live client connection attempt** once 1C lands (2D).
- **Policy call:** whether the server verifies TribesNext certificates (real accounts only) or
  admits any client.

## Verification gates

Every task above states a falsifiable check. Two standards are worth repeating because they are
what caught real errors here:

- **Cross-corroboration beats round-tripping.** The packet prefix was believed because two
  independently-written fields in opposite directions agreed across thousands of packets — not
  because a decoder agreed with itself.
- **Perturbation controls.** When a decode "fits", break it deliberately and confirm the fit
  collapses. A layout that survives ±1 bit was never evidence.

## Fast path to a playable server

If the goal is a stock client connecting and moving correctly, in order:

1. **1C** (RSA) — nothing attaches without it
2. **1A** (resist) — otherwise movement is wrong in a way that looks like lag
3. **1E + 2C** (prefix + wiring) — real packets in and out
4. **1D** (clipper) — collisions match
5. **2A** (scoping) — the client is actually sent a world
6. **2B** (query) — findable in the browser

The script VM (3B–3E) is not on this path. A server can run stock rules without executing
TorqueScript; the VM is what makes *mods* work.
