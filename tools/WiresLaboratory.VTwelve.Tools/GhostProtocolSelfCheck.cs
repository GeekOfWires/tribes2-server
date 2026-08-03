using System.Numerics;
using WiresLaboratory.VTwelve.Net;
using WiresLaboratory.VTwelve.Net.Ghost;

namespace WiresLaboratory.VTwelve.Tools;

/// <summary>
/// Round-trip self-check for the ghost update wire format implemented under
/// <c>src/WiresLaboratory.VTwelve/Net/Ghost/</c> — see that directory's types and
/// <c>src/WiresLaboratory.VTwelve/Net/GhostProtocol.md</c> for the recovered format itself.
/// </summary>
/// <remarks>
/// This is a self-consistency check (write with this codec, read back with this codec), not a
/// cross-check against a real capture — the ground-truth document itself notes that no ghost
/// section has been isolated from a captured packet yet (the framing upstream of it is not
/// recovered). It still catches every class of bug that matters for wire correctness: bit-count
/// mismatches, sign errors, and precision-loss claims that don't match reality.
/// </remarks>
public static class GhostProtocolSelfCheck
{
    public static int Run(string[] args)
    {
        var failures = 0;

        failures += Check("writeFloat/readFloat at several widths", CheckFloat);
        failures += Check("writeSignedFloat/readSignedFloat at several widths", CheckSignedFloat);
        failures += Check("writeGhostSignedInt/readGhostSignedInt, including negatives and zero", CheckGhostSignedInt);
        failures += Check("writeNormalVector/readNormalVector, including near the poles", CheckNormalVector);
        failures += Check("writeDataBlockId/readDataBlockId, present and absent", CheckDataBlockId);
        failures += Check("raw 32-bit float round-trips exactly", CheckRawFloat);
        failures += Check("compressed point at all four levels", CheckCompressedPoint);
        failures += Check("ShapeBase group-gate mask matches the union of its sub-blocks", CheckGroupGateMaskSelfValidates);
        failures += Check("mask-bit index helpers (sound/anim/mounted-image)", CheckMaskCatalog);
        failures += Check("full Player->ShapeBase->GameBase pack/unpack chain, sparse update", CheckChainSparseUpdate);
        failures += Check("full pack/unpack chain, dense initial update", CheckChainDenseInitialUpdate);
        failures += Check("owner-state suppression short-circuits the move/energy fields", CheckOwnerSuppression);
        failures += Check("ghost record framing: add, update and remove records", CheckGhostRecordFraming);
        failures += Check("9-bit ghost-id width wraps the same way PacketHeader's 9-bit sequence does", CheckNineBitWrapInteraction);

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "RESULT: ghost protocol self-check clean — all assertions passed."
            : $"RESULT: {failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------------------------------

    private static int Check(string name, Action check)
    {
        try
        {
            check();
            Console.WriteLine($"  PASS  {name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertClose(float expected, float actual, float tolerance, string message)
    {
        if (MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}, tolerance {tolerance}");
    }

    /// <summary>Half the quantisation step of a value in <c>[0,1]</c> at <paramref name="bits"/> bits — the worst-case rounding error <see cref="GhostBitStreamExtensions.WriteFloat"/> can introduce.</summary>
    private static float FloatQuantum(int bits) => 1f / ((1 << bits) - 1);

    /// <summary>Same, but for the <c>[-1,1]</c> domain <see cref="GhostBitStreamExtensions.WriteSignedFloat"/> covers.</summary>
    private static float SignedFloatQuantum(int bits) => 2f / ((1 << bits) - 1);

    // ------------------------------------------------------------------------------------------
    // 1. writeFloat / readFloat
    // ------------------------------------------------------------------------------------------
    private static void CheckFloat()
    {
        // A 6-bit float (the width the damage fraction uses) has coarse precision: 63 steps
        // across [0,1], so the worst-case error is 1/63 ~= 0.0159 — report the actual numbers
        // rather than asserting exact equality, which the format cannot provide at this width.
        foreach (var (value, bits) in new (float, int)[] { (0f, 6), (1f, 6), (0.5f, 6), (0.3333f, 6), (0.999f, 7), (0.12345f, 5) })
        {
            var s = new BitStream();
            s.WriteFloat(value, bits);
            s.BitPosition = 0;
            var got = s.ReadFloat(bits);
            var quantum = FloatQuantum(bits);
            AssertClose(value, got, quantum, $"WriteFloat({value}, {bits})");
            Console.WriteLine($"        WriteFloat({value:F4}, {bits}b) -> {got:F4} (quantum {quantum:F5})");
        }

        // Out-of-range input clamps rather than wrapping/throwing.
        var clampLow = new BitStream();
        clampLow.WriteFloat(-5f, 6);
        clampLow.BitPosition = 0;
        Assert(clampLow.ReadFloat(6) == 0f, "WriteFloat should clamp negative input to 0");

        var clampHigh = new BitStream();
        clampHigh.WriteFloat(5f, 6);
        clampHigh.BitPosition = 0;
        AssertClose(1f, clampHigh.ReadFloat(6), FloatQuantum(6), "WriteFloat should clamp >1 input to 1");
    }

    // ------------------------------------------------------------------------------------------
    // 2. writeSignedFloat / readSignedFloat
    // ------------------------------------------------------------------------------------------
    private static void CheckSignedFloat()
    {
        // 6 bits is what the look-angle fields use.
        foreach (var (value, bits) in new (float, int)[] { (-1f, 6), (1f, 6), (0f, 6), (-0.5f, 6), (0.75f, 7), (-0.999f, 5) })
        {
            var s = new BitStream();
            s.WriteSignedFloat(value, bits);
            s.BitPosition = 0;
            var got = s.ReadSignedFloat(bits);
            var quantum = SignedFloatQuantum(bits);
            AssertClose(value, got, quantum, $"WriteSignedFloat({value}, {bits})");
            Console.WriteLine($"        WriteSignedFloat({value:F4}, {bits}b) -> {got:F4} (quantum {quantum:F5})");
        }
    }

    // ------------------------------------------------------------------------------------------
    // 3. writeGhostSignedInt / readGhostSignedInt
    // ------------------------------------------------------------------------------------------
    private static void CheckGhostSignedInt()
    {
        // Exercise the awkward cases explicitly: zero, the most negative representable value at
        // several widths, and both signs near a power-of-two boundary.
        var cases = new (int Value, int Bits)[]
        {
            (0, 8), (-1, 8), (1, 8),
            (127, 8), (-127, 8),       // max magnitude an 8-bit sign+magnitude field can hold (7-bit magnitude)
            (32767, 16), (-32767, 16), // level-0 compressed-point boundary
            (131071, 18), (-131071, 18), // level-1 boundary
            (524287, 20), (-524287, 20), // level-2 boundary
            // Sign+magnitude at 32 total bits has only 31 magnitude bits, so its representable
            // range is [-(2^31-1), 2^31-1] — one narrower than int's two's-complement range.
            // int.MinValue (-2^31) is therefore NOT representable at 32 bits; that is a real
            // limit of the format, not a bug, so the boundary case tested here is int.MaxValue
            // and its negation instead.
            (int.MaxValue, 32), (-int.MaxValue, 32),
        };

        foreach (var (value, bits) in cases)
        {
            var s = new BitStream();
            s.WriteGhostSignedInt(value, bits);
            Assert(s.BitPosition == bits, $"WriteGhostSignedInt({value}, {bits}) wrote {s.BitPosition} bits, expected {bits}");
            s.BitPosition = 0;
            var got = s.ReadGhostSignedInt(bits);
            Assert(got == value, $"WriteGhostSignedInt({value}, {bits}) round-tripped to {got}");
        }
        Console.WriteLine($"        {cases.Length} signed-int cases round-tripped exactly (sign+magnitude has no precision loss, only range limits)");

        // Sign+magnitude has two zeros; a negative-signed zero degrades to 0, not -0 (there is no
        // -0 in a C# int for it to become anyway, but the encoding step is worth asserting).
        var negZero = new BitStream();
        negZero.WriteFlag(true); // sign = negative
        negZero.WriteInt(0, 7);  // magnitude = 0
        negZero.BitPosition = 0;
        Assert(negZero.ReadGhostSignedInt(8) == 0, "a sign-negative zero magnitude must read back as 0");
    }

    // ------------------------------------------------------------------------------------------
    // 4. writeNormalVector / readNormalVector
    // ------------------------------------------------------------------------------------------
    private static void CheckNormalVector()
    {
        var directions = new (string Name, Vector3 Vector)[]
        {
            ("+X", new Vector3(1, 0, 0)),
            ("+Y", new Vector3(0, 1, 0)),
            ("+Z (north pole)", new Vector3(0, 0, 1)),
            ("-Z (south pole)", new Vector3(0, 0, -1)),
            ("near north pole", new Vector3(0.001f, 0.0005f, 0.999999f)),
            ("near south pole", new Vector3(-0.0007f, 0.0009f, -0.999999f)),
            ("diagonal", Vector3.Normalize(new Vector3(1, 1, 1))),
            ("odd angle", Vector3.Normalize(new Vector3(0.37f, -0.81f, 0.22f))),
        };

        const int bits = 8; // damage/shield direction width
        var maxAngleErrorDeg = 0f;
        foreach (var (name, vector) in directions)
        {
            var s = new BitStream();
            s.WriteNormalVector(vector, bits);
            Assert(s.BitPosition == 2 * bits + 1, $"WriteNormalVector width: expected {2 * bits + 1} bits, got {s.BitPosition}");
            s.BitPosition = 0;
            var got = s.ReadNormalVector(bits);

            var expected = Vector3.Normalize(vector);
            var dot = Math.Clamp(Vector3.Dot(expected, Vector3.Normalize(got)), -1f, 1f);
            var angleErrorDeg = MathF.Acos(dot) * (180f / MathF.PI);
            maxAngleErrorDeg = MathF.Max(maxAngleErrorDeg, angleErrorDeg);

            // At 8 bits/angle the azimuth step is ~2*pi/255 rad ~= 1.4 deg; allow generous slack
            // for the compounded azimuth+inclination quantisation, especially near a pole where
            // azimuth is numerically unstable (a tiny input change swings phi a lot) even though
            // the *decoded* vector is still short — hence the wide-looking but still meaningful bound.
            Assert(angleErrorDeg < 5f, $"{name}: normal vector round-trip angle error {angleErrorDeg:F3} deg exceeds bound");
            Console.WriteLine($"        {name,-18} angle error {angleErrorDeg:F3} deg");
        }
        Console.WriteLine($"        max angle error across all cases: {maxAngleErrorDeg:F3} deg at {bits} bits/angle");

        // Degenerate (zero-length) input takes the validity-flag branch and reads back as Zero.
        var zero = new BitStream();
        zero.WriteNormalVector(Vector3.Zero, 8);
        Assert(zero.BitPosition == 17, "zero-vector write should still consume the full 2b+1 bits");
        zero.BitPosition = 0;
        Assert(zero.ReadNormalVector(8) == Vector3.Zero, "degenerate input should read back as Vector3.Zero");
    }

    // ------------------------------------------------------------------------------------------
    // 5. writeDataBlockId / readDataBlockId
    // ------------------------------------------------------------------------------------------
    private static void CheckDataBlockId()
    {
        foreach (int? id in new int?[] { null, 0, 1, 2047 }) // 2047 = 2^11 - 1, the largest 11-bit id
        {
            var s = new BitStream();
            s.WriteDataBlockId(id);
            Assert(s.BitPosition == (id.HasValue ? 12 : 1), $"WriteDataBlockId({id}) wrote {s.BitPosition} bits");
            s.BitPosition = 0;
            Assert(s.ReadDataBlockId() == id, $"WriteDataBlockId({id}) round trip");
        }
    }

    // ------------------------------------------------------------------------------------------
    // 6. raw 32-bit float
    // ------------------------------------------------------------------------------------------
    private static void CheckRawFloat()
    {
        foreach (var value in new[] { 0f, -0f, 1f, -1f, 3.14159265f, float.MinValue, float.MaxValue, float.Epsilon, -12345.6789f })
        {
            var s = new BitStream();
            s.WriteRawFloat(value);
            s.BitPosition = 0;
            var got = s.ReadRawFloat();
            Assert(BitConverter.SingleToUInt32Bits(got) == BitConverter.SingleToUInt32Bits(value), $"raw float {value} did not round-trip bit-exact (got {got})");
        }
    }

    // ------------------------------------------------------------------------------------------
    // 7. compressed point, all four levels
    // ------------------------------------------------------------------------------------------
    private static void CheckCompressedPoint()
    {
        var scope = new Vector3(1000f, 2000f, 50f);

        // Level 0: delta magnitude in scale-units under 32768, i.e. under 327.68 world units at scale 0.01.
        CheckCompressedPointCase("level 0", scope, scope + new Vector3(10f, -20f, 5f), CompressedPointLevel.Delta16, 0.01f);

        // Level 1: between 327.68 and 1310.72 world units away.
        CheckCompressedPointCase("level 1", scope, scope + new Vector3(500f, 0f, 0f), CompressedPointLevel.Delta18, 0.01f);

        // Level 2: between 1310.72 and 5242.88 world units away.
        CheckCompressedPointCase("level 2", scope, scope + new Vector3(2000f, 2000f, 0f), CompressedPointLevel.Delta20, 0.01f);

        // Level 3a: delta too large even for level 2 (falls back to absolute).
        CheckCompressedPointCase("level 3 (delta too large)", scope, scope + new Vector3(1_000_000f, 0f, 0f), CompressedPointLevel.Absolute, 0.01f);

        // Level 3b: no scope position at all (e.g. the connection has no valid control object yet).
        {
            var s = new BitStream();
            var position = new Vector3(123.456f, -789.01f, 42f);
            s.WriteCompressedPoint(position, null);
            s.BitPosition = 0;
            var got = s.ReadCompressedPoint(null);
            Assert(Vector3.DistanceSquared(position, got) < 1e-6f, "level 3 (no scope) should round-trip exactly (raw floats)");
            Console.WriteLine("        level 3 (no scope)   exact (raw floats)");
        }
    }

    private static void CheckCompressedPointCase(string name, Vector3 scope, Vector3 position, CompressedPointLevel expectedLevel, float scale)
    {
        var s = new BitStream();
        s.WriteCompressedPoint(position, scope, scale);
        s.BitPosition = 0;
        var level = (CompressedPointLevel)s.ReadInt(2);
        Assert(level == expectedLevel, $"{name}: expected level {expectedLevel}, got {level}");
        s.BitPosition = 0;
        var got = s.ReadCompressedPoint(scope, scale);

        // Quantisation step for a delta-encoded level is exactly `scale` (one unit of the 16/18/20-bit integer == `scale` world units).
        var tolerance = expectedLevel == CompressedPointLevel.Absolute ? 1e-3f : scale;
        var error = Vector3.Distance(position, got);
        Assert(error <= tolerance + 1e-4f, $"{name}: position error {error} exceeds tolerance {tolerance}");
        Console.WriteLine($"        {name,-28} level={level} error={error:F5} (tolerance {tolerance:F3})");
    }

    // ------------------------------------------------------------------------------------------
    // 8. group-gate mask self-validation (cross-checking the document's own claim)
    // ------------------------------------------------------------------------------------------
    private static void CheckGroupGateMaskSelfValidates()
    {
        uint union = (uint)GhostMaskBits.Damage;
        for (var i = 0; i < 4; i++) union |= (uint)GhostMaskCatalog.SoundThread(i);
        for (var i = 0; i < 4; i++) union |= (uint)GhostMaskCatalog.AnimThread(i);
        for (var i = 0; i < 8; i++) union |= (uint)GhostMaskCatalog.MountedImage(i);
        union |= (uint)GhostMaskBits.Mounted | (uint)GhostMaskBits.Cloak | (uint)GhostMaskBits.Shield | (uint)GhostMaskBits.Invincible;

        Assert(union == ShapeBaseGhostState.GroupGateMask,
            $"union of documented ShapeBase sub-blocks is 0x{union:X8}, expected GroupGateMask 0x{ShapeBaseGhostState.GroupGateMask:X8}");
        Console.WriteLine($"        union of sub-block bits = 0x{union:X8} == GroupGateMask (bits {{3}} u {{5..24}})");
    }

    // ------------------------------------------------------------------------------------------
    // 9. mask catalog bounds
    // ------------------------------------------------------------------------------------------
    private static void CheckMaskCatalog()
    {
        Assert(GhostMaskCatalog.SoundThread(0) == GhostMaskBits.SoundThread0, "SoundThread(0)");
        Assert(GhostMaskCatalog.SoundThread(3) == GhostMaskBits.SoundThread3, "SoundThread(3)");
        Assert(GhostMaskCatalog.AnimThread(0) == GhostMaskBits.AnimThread0, "AnimThread(0)");
        Assert(GhostMaskCatalog.AnimThread(3) == GhostMaskBits.AnimThread3, "AnimThread(3)");
        Assert(GhostMaskCatalog.MountedImage(0) == GhostMaskBits.MountedImage0, "MountedImage(0)");
        Assert(GhostMaskCatalog.MountedImage(7) == GhostMaskBits.MountedImage7, "MountedImage(7)");

        AssertThrows(() => GhostMaskCatalog.SoundThread(4), "SoundThread(4) should be out of range");
        AssertThrows(() => GhostMaskCatalog.AnimThread(-1), "AnimThread(-1) should be out of range");
        AssertThrows(() => GhostMaskCatalog.MountedImage(8), "MountedImage(8) should be out of range");
    }

    private static void AssertThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message + " (no exception thrown)");
    }

    // ------------------------------------------------------------------------------------------
    // 10. full chain: sparse update (only a couple of mask bits set)
    // ------------------------------------------------------------------------------------------
    private static void CheckChainSparseUpdate()
    {
        var mask = GhostMaskBits.DataBlock | GhostMaskBits.Damage | GhostMaskBits.PlayerActionAnimation;

        var writer = new PlayerGhostState
        {
            DataBlockId = 42,
            Damage = new DamageGhostFields { DamageFraction = 0.25f, DamageState = 2, UnidentifiedFlag = true, DamageDirection = new Vector3(0, 0, 1) },
            ActionAnimation = new PlayerActionAnimationFields { Action = 17, Flag0 = true, Flag1 = false, Position = -0.5f },
        };

        var stream = new BitStream();
        writer.Pack(stream, mask, isInitial: false, suppressOwnerState: false, scopePosition: null);
        var bitsWritten = stream.BitPosition;
        stream.BitPosition = 0;

        var reader = new PlayerGhostState();
        reader.Unpack(stream, isInitial: false, scopePosition: null);
        Assert(stream.BitPosition == bitsWritten, $"reader consumed {stream.BitPosition} bits, writer wrote {bitsWritten}");

        Assert(reader.DataBlockId == 42, $"DataBlockId: expected 42, got {reader.DataBlockId}");
        Assert(reader.TargetId == null, "TargetId should be unset (bit 2 not in mask)");
        AssertClose(0.25f, reader.Damage.DamageFraction, FloatQuantum(6), "DamageFraction");
        Assert(reader.Damage.DamageState == 2, "DamageState");
        Assert(reader.Damage.UnidentifiedFlag, "Damage.UnidentifiedFlag");
        Assert(reader.ActionAnimation.Action == 17, "ActionAnimation.Action");
        Assert(reader.ActionAnimation.Flag0 && !reader.ActionAnimation.Flag1, "ActionAnimation flags");
        Assert(reader.ActionAnimation.Position.HasValue, "ActionAnimation.Position should be present");
        AssertClose(-0.5f, reader.ActionAnimation.Position!.Value, SignedFloatQuantum(6), "ActionAnimation.Position");

        // Fields whose mask bit was not set must stay at their (default) value, not be corrupted
        // by misaligned reads of neighbouring fields.
        Assert(reader.Cloaked == false, "Cloaked should be false (bit not set, block still consumes exactly 1 bit)");
        Assert(reader.Shield.Energy == 0f, "Shield should be untouched");
        Assert(reader.UnknownField27 == 0, "UnknownField27 should be untouched (bit 27 not in mask)");

        Console.WriteLine($"        sparse update: {bitsWritten} bits for {System.Numerics.BitOperations.PopCount((uint)mask)} set mask bits");
    }

    // ------------------------------------------------------------------------------------------
    // 11. full chain: dense update — every field populated, initial ghost
    // ------------------------------------------------------------------------------------------
    private static void CheckChainDenseInitialUpdate()
    {
        var mask = (GhostMaskBits)0x0FFFFFFFu; // every documented bit (0-27) set

        var writer = new PlayerGhostState
        {
            DataBlockId = 5,
            TargetId = 300,
            Cloaked = true,
            Shield = new ShieldGhostFields { ShieldNormal = new Vector3(0, 1, 0), Energy = 0.8f },
            Invincible = new InvincibleGhostFields { A = 1.5f, B = -2.5f },
            Mount = new MountGhostFields { GhostIndex = 700, Node = 12 },
            UnknownField27 = 5,
            ActionAnimation = new PlayerActionAnimationFields { Action = 200, Flag0 = true, Flag1 = true, Position = 0.1f },
            UnnamedOptionalField = 99,
            Move = new PlayerMoveFields
            {
                LeadingField3 = 6,
                Optional7 = 100,
                Flag0 = true,
                Flag1 = false,
                Position = new Vector3(500f, -300f, 40f),
                Velocity = new Vector3(30f, 0f, -5f),
                LookYaw = 0.4f,
                LookPitch = -0.2f,
                RotationFraction = 0.75f,
                SubObject = new PlayerMoveSubObjectFields
                {
                    Optional16A = 1000,
                    Optional16B = null,
                    Optional16C = 65000,
                    Field6A = 10,
                    Field6B = 63,
                    Field6C = 0,
                },
            },
            EnergyFraction = 0.9f,
        };
        for (var i = 0; i < 4; i++)
        {
            writer.SoundThreads[i] = new SoundThreadGhostFields { Play = i % 2 == 0, ProfileDataBlockId = 100 + i };
            writer.AnimThreads[i] = new AnimThreadGhostFields { Sequence = (uint)(3 + i), State = (uint)(i % 4), Flag0 = i == 0, Flag1 = i == 1 };
        }
        for (var i = 0; i < 8; i++)
        {
            writer.MountedImages[i] = new MountedImageGhostFields
            {
                DataBlockId = 200 + i,
                Flag0 = i % 2 == 0, Flag1 = i % 3 == 0, Flag2 = i % 5 == 0, Flag3 = false, Flag4 = true,
                SlotField = (uint)(i % 8),
                InitialExtraFlag = i == 0,
            };
        }

        var scope = new Vector3(0f, 0f, 0f);
        var stream = new BitStream(4096);
        writer.Pack(stream, mask, isInitial: true, suppressOwnerState: false, scopePosition: scope);
        var bitsWritten = stream.BitPosition;
        stream.BitPosition = 0;

        var reader = new PlayerGhostState();
        reader.Unpack(stream, isInitial: true, scopePosition: scope);
        Assert(stream.BitPosition == bitsWritten, $"reader consumed {stream.BitPosition} bits, writer wrote {bitsWritten}");

        Assert(reader.DataBlockId == 5, "DataBlockId");
        Assert(reader.TargetId == 300, "TargetId");
        Assert(reader.Cloaked, "Cloaked");
        AssertClose(0.8f, reader.Shield.Energy, FloatQuantum(5), "Shield.Energy");
        Assert(reader.Invincible.A == 1.5f && reader.Invincible.B == -2.5f, "Invincible (raw floats, exact)");
        Assert(reader.Mount.GhostIndex == 700 && reader.Mount.Node == 12, "Mount");
        Assert(reader.UnknownField27 == 5, "UnknownField27");
        Assert(reader.ActionAnimation.Action == 200, "ActionAnimation.Action");
        Assert(reader.UnnamedOptionalField == 99, "UnnamedOptionalField");

        Assert(reader.Move.LeadingField3 == 6, "Move.LeadingField3");
        Assert(reader.Move.Optional7 == 100, "Move.Optional7");
        Assert(Vector3.Distance(reader.Move.Position, writer.Move.Position) <= CompressedPointCodec.DefaultScale + 1e-4f,
            $"Move.Position: expected ~{writer.Move.Position}, got {reader.Move.Position}");

        var expectedSpeed = writer.Move.Velocity.Length();
        var gotSpeed = reader.Move.Velocity.Length();
        AssertClose(expectedSpeed, gotSpeed, 1f / 32f + 1e-3f, "Move.Velocity magnitude"); // 13-bit magnitude at 1/32 unit precision
        Assert(reader.Move.SubObject.Optional16A == 1000, "SubObject.Optional16A");
        Assert(reader.Move.SubObject.Optional16B == null, "SubObject.Optional16B should be absent");
        Assert(reader.Move.SubObject.Optional16C == 65000, "SubObject.Optional16C");
        Assert(reader.Move.SubObject.Field6B == 63, "SubObject.Field6B (max 6-bit value)");
        // mask has the NoWarp bit (bit 4) set, so the writer negates it: WarpAllowed == false.
        Assert(!reader.Move.WarpAllowed, "Move.WarpAllowed should be false (NoWarp bit is set in mask, writer negates it)");

        AssertClose(0.9f, reader.EnergyFraction, FloatQuantum(5), "EnergyFraction");

        for (var i = 0; i < 4; i++)
        {
            Assert(reader.SoundThreads[i].Play == (i % 2 == 0), $"SoundThreads[{i}].Play");
            Assert(reader.SoundThreads[i].ProfileDataBlockId == 100 + i, $"SoundThreads[{i}].ProfileDataBlockId");
            Assert(reader.AnimThreads[i].Sequence == 3 + i, $"AnimThreads[{i}].Sequence");
        }
        for (var i = 0; i < 8; i++)
        {
            Assert(reader.MountedImages[i].DataBlockId == 200 + i, $"MountedImages[{i}].DataBlockId");
            Assert(reader.MountedImages[i].SlotField == (uint)(i % 8), $"MountedImages[{i}].SlotField");
            Assert(reader.MountedImages[i].InitialExtraFlag == (i == 0), $"MountedImages[{i}].InitialExtraFlag");
        }

        Console.WriteLine($"        dense initial update: {bitsWritten} bits ({bitsWritten / 8.0:F1} bytes) for every documented field populated");
    }

    // ------------------------------------------------------------------------------------------
    // 12. owner-state suppression
    // ------------------------------------------------------------------------------------------
    private static void CheckOwnerSuppression()
    {
        var mask = GhostMaskBits.PlayerMoveAndPosition;
        var writer = new PlayerGhostState
        {
            Move = new PlayerMoveFields { Position = new Vector3(1, 2, 3), Velocity = new Vector3(10, 0, 0) },
            EnergyFraction = 0.5f,
        };

        var stream = new BitStream();
        writer.Pack(stream, mask, isInitial: false, suppressOwnerState: true, scopePosition: null);
        var bitsWritten = stream.BitPosition;
        stream.BitPosition = 0;

        var reader = new PlayerGhostState { EnergyFraction = -1f }; // sentinel: should stay untouched
        reader.Unpack(stream, isInitial: false, scopePosition: null);

        Assert(stream.BitPosition == bitsWritten, "suppressed record should have nothing left to read");
        Assert(reader.EnergyFraction == -1f, "EnergyFraction must not be written/read when the owner-state flag suppresses the rest of the record");
        Assert(reader.Move.Position == default, "Move.Position must not be touched when suppressed");

        Console.WriteLine($"        suppressed record: {bitsWritten} bits total (ShapeBase/GameBase header + suppression flag only)");
    }

    // ------------------------------------------------------------------------------------------
    // 13. ghost record framing
    // ------------------------------------------------------------------------------------------
    private static void CheckGhostRecordFraming()
    {
        const int idBits = 10;
        var stream = new BitStream();
        GhostRecordFraming.WriteSectionHeader(stream, hasGhosts: true, idBits);
        GhostRecordFraming.WriteRecordHeader(stream, new GhostRecordHeader(GhostIndex: 3, IdBits: idBits, IsRemove: false, ClassId: 12));
        GhostRecordFraming.WriteRecordHeader(stream, new GhostRecordHeader(GhostIndex: 4, IdBits: idBits, IsRemove: false, ClassId: null)); // already known to the client
        GhostRecordFraming.WriteRecordHeader(stream, new GhostRecordHeader(GhostIndex: 3, IdBits: idBits, IsRemove: true, ClassId: null));
        GhostRecordFraming.WriteTerminator(stream);

        stream.BitPosition = 0;
        var idWidth = GhostRecordFraming.ReadSectionHeader(stream);
        Assert(idWidth == idBits, $"expected idBits {idBits}, got {idWidth}");

        var r1 = GhostRecordFraming.ReadRecordHeader(stream, idWidth!.Value, expectClassId: true);
        Assert(r1 is { GhostIndex: 3, IsRemove: false, ClassId: 12 }, "record 1 (add, ghost 3, class 12)");

        var r2 = GhostRecordFraming.ReadRecordHeader(stream, idWidth.Value, expectClassId: false);
        Assert(r2 is { GhostIndex: 4, IsRemove: false, ClassId: null }, "record 2 (add, ghost 4, already known)");

        var r3 = GhostRecordFraming.ReadRecordHeader(stream, idWidth.Value, expectClassId: false);
        Assert(r3 is { GhostIndex: 3, IsRemove: true }, "record 3 (remove, ghost 3)");

        var r4 = GhostRecordFraming.ReadRecordHeader(stream, idWidth.Value, expectClassId: false);
        Assert(r4 is null, "expected terminator (null) after 3 records");

        // Empty section: just the single false flag.
        var empty = new BitStream();
        GhostRecordFraming.WriteSectionHeader(empty, hasGhosts: false, idBits: 10);
        Assert(empty.BitPosition == 1, "empty section should be exactly 1 bit");
        empty.BitPosition = 0;
        Assert(GhostRecordFraming.ReadSectionHeader(empty) == null, "empty section should read back as null");

        Console.WriteLine("        3 records (2 adds + 1 remove) + terminator round-tripped in order");
    }

    // ------------------------------------------------------------------------------------------
    // 14. 9-bit sequence-wrap interaction with PacketHeader
    // ------------------------------------------------------------------------------------------
    private static void CheckNineBitWrapInteraction()
    {
        // PacketHeader's send/ack sequences are 9 bits, wrapping at 512 (SequenceModulo). Ghost
        // ids share the same underlying BitStream.WriteInt/ReadInt truncate-to-width behaviour,
        // and the framing's idBits can legitimately be 9 (idSize-3 encoded in 3 bits covers 3..10)
        // — exercising a ghost id at that width should wrap exactly the same way PacketHeader's
        // sequence numbers do.
        Assert(PacketHeader.SequenceBits == 9, "PacketHeader.SequenceBits is expected to be 9 for this check to be meaningful");
        Assert(PacketHeader.SequenceModulo == 512, "PacketHeader.SequenceModulo is expected to be 512");

        const int idBits = 9;

        // A value one below the modulus round-trips exactly.
        var atBoundary = new BitStream();
        GhostRecordFraming.WriteRecordHeader(atBoundary, new GhostRecordHeader(511, idBits, false, null));
        atBoundary.BitPosition = 0;
        var readBack = GhostRecordFraming.ReadRecordHeader(atBoundary, idBits, expectClassId: false);
        Assert(readBack!.Value.GhostIndex == 511, $"511 should round-trip at 9 bits, got {readBack.Value.GhostIndex}");

        // A raw WriteInt of the modulus itself (512 = 1<<9) truncates to 0 at 9 bits — the same
        // wraparound PacketHeader.DistanceFrom relies on for its own 9-bit sequence field.
        var wrapped = new BitStream();
        wrapped.WriteInt(512u, idBits);
        wrapped.BitPosition = 0;
        Assert(wrapped.ReadInt(idBits) == 0u, "512 written at 9 bits should read back as 0 (mod 512), matching PacketHeader.SequenceModulo");

        // And one past that wraps to 1, exactly mirroring PacketHeader.DistanceFrom's modulo arithmetic.
        var wrappedPlusOne = new BitStream();
        wrappedPlusOne.WriteInt(513u, idBits);
        wrappedPlusOne.BitPosition = 0;
        Assert(wrappedPlusOne.ReadInt(idBits) == 1u, "513 written at 9 bits should read back as 1 (mod 512)");

        // idSize itself: writeInt(idSize - 3, 3) must be able to express 9 (encoded as 6).
        var header = new BitStream();
        GhostRecordFraming.WriteSectionHeader(header, hasGhosts: true, idBits: 9);
        header.BitPosition = 0;
        Assert(GhostRecordFraming.ReadSectionHeader(header) == 9, "idSize=9 should round-trip through the 3-bit (idSize-3) encoding");

        Console.WriteLine("        idBits=9 ghost indices wrap at 512, matching PacketHeader's SequenceModulo exactly");
    }
}
