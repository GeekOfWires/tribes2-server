# TribesNext handshake authentication

How the community patch's account authentication rides inside the engine's connect handshake,
and what a managed server must do to admit a stock client.

This resolves what was the project's hard blocker: the server-authored trailer in
`ConnectChallengeResponse`. It is decoded, and the answer is better than expected.

## The headline: the server needs no secret

The `0x1e` trailer is a random challenge **RSA-encrypted under the *client's* public key** —
and the client hands the server that public key inside its own `0x1a` request. The server
encrypts with the client's public exponent; the client proves who it is by decrypting with the
private key only it holds.

**The server holds no private material in this exchange.** The earlier working hypothesis —
that a managed server would need a key registered with the TribesNext master — is wrong for
the handshake. (A master key matters only for *minting* account certificates, i.e. account
creation, which is a separate service and not needed to admit existing clients.)

## Why this looked like noise

The payloads are **bit-packed LSB-first and not byte-aligned**. Byte-level inspection sees
high-entropy garbage; read as a bitstream they are entirely structured. This is the same bit
convention `Net/BitStream.cs` already implements.

Confirmed independently here by decoding the committed fixture: the certificate parses with
**zero leftover bytes** only when extracted *unaligned*. An aligned read fails.

## `ConnectChallengeRequest` (0x1a, client to server)

After the type byte:

```
u32   protocol / nonce
u32   client token
str   empty string        (1 flag bit + 8-bit length)
bit   flag
bit   flag = 1            -> an auth certificate follows
u8    marker = 0x54 ('T')
u64   client challenge    (per session, random)
u32   version id          (build constant; identical across sessions)
u12   certificate length
raw   certificate
```

The certificate is a binary TribesNext account cert:

```
ASCII  user \t guid \t <elen> \t <nlen> \t
raw    e    (elen bytes; observed exponent 5)
raw    n    (nlen bytes; 129-byte modulus observed)
raw    sig  (remainder; 512 bytes = 4096-bit signature by the TribesNext authority)
```

Verified against both captured sessions: identical structure, clean decode, and only the client
token and the 64-bit challenge differ between them.

## `ConnectChallengeResponse` (0x1e, server to client) — the former blocker

```
u32  protocol, u32 session token, u32 client token   (the already-known 13-byte header)
bit  flag = 1
u9   length = 128
raw  block  (128 bytes, equal to the client's modulus size, value < n)
```

The block is `RSA(challenge, e, n)` under the **client's** cert key. The plaintext carries the
client's own challenge followed by a server-generated challenge; the client decrypts, looks for
its own challenge, and takes what follows as the server's.

## `ConnectRequest` (0x20, client to server)

After the three known tokens: a nonce, a flag, then an echo of the server challenge, then a
count and the connect arguments (name, skin, voice). The "fixed 28-byte tail" noticed earlier
across sessions is those build-constant connect arguments — not cryptographic material.

## What a managed server must implement

1. Parse the `0x1a` certificate for the client's public key and its 64-bit challenge.
2. Generate a random server challenge.
3. Emit `0x1e` as flag + 9-bit length + the RSA block, encrypted under the client's key,
   bit-packed LSB-first.
4. On `0x20`, compare the echoed server challenge; on match, send `0x24`.

All arithmetic uses the client's **public** key. A stock client always presents a validly signed
certificate, so it completes this exchange with such a server.

## Certificate signature checking is an authorization policy

The reference server verifies the certificate's 4096-bit signature against the TribesNext
authority's public key *before* issuing a challenge: presented with a genuine certificate it
returns the challenge block; with a tampered signature, username or guid it returns flag = 0 and
no block.

That check uses the authority's **public** key, which ships with the patch. A managed server can
therefore choose: perform the check to restrict play to real TribesNext accounts, or skip it and
admit any client. Either way no server secret is involved. This is an operator policy decision
about one's own server, not a protocol requirement.

## Confidence

**Confirmed:** the bit-packed LSB-first framing; the full `0x1a` layout including certificate
structure; `0x1e` as flag + 9-bit length + RSA block sized to the client modulus; that the block
is derived from the client's certificate key plus a server random; that the server gates it on
certificate validity; that the client never asks the server to prove anything; and that **the
server needs no secret**.

**Inferred (strong):** the exact plaintext ordering inside the block, from the client-side
search-and-extract behaviour; that `0x20`'s fixed tail is build-constant connect arguments.

**Not determined:** the precise padding and field order *inside* the RSA block — establishing
that would require an account private key to decrypt, which was correctly not attempted; the
location of the embedded authority modulus as raw bytes (its existence is demonstrated
behaviourally); and whether the engine's challenge includes anti-replay material as the
script-level implementation does.

## Scope note

This analysis covers making our own server speak the protocol. It does not describe, and must
not be extended to, forging certificates or impersonating accounts — the mechanism exists to
authenticate players to a central service, and defeating it is out of scope.
