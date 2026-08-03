using System.Numerics;

namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>
/// The <see cref="BitStream"/> primitives the ghost update format needs, on top of the generic
/// <c>WriteFlag</c>/<c>WriteInt</c> the base type already has. These are recovered from
/// <c>src/WiresLaboratory.VTwelve/Net/GhostProtocol.md</c> (the "BitStream primitives" table) and
/// added here — deliberately not in <c>BitStream.cs</c> itself, which other work depends on
/// staying untouched.
/// </summary>
/// <remarks>
/// <para>
/// <b>Naming note.</b> <see cref="BitStream"/> already exposes a member called
/// <c>WriteSignedInt</c>, but it is a plain two's-complement write
/// (<c>WriteInt(unchecked((uint)value), bitCount)</c>) — a different encoding from the one this
/// document recovers for the engine's <c>writeSignedInt</c> at <c>0x0043c0a0</c>, which is
/// sign-and-magnitude: a flag bit for the sign, then the absolute value in <c>bitCount-1</c>
/// bits. Reusing the name <c>WriteSignedInt</c> as an extension method would compile, but would
/// never actually run — C# always prefers an applicable instance method over an extension method
/// with the same name, so <c>stream.WriteSignedInt(v, n)</c> would silently keep calling
/// <see cref="BitStream"/>'s two's-complement version. The ghost-format primitive is therefore
/// named <see cref="WriteGhostSignedInt"/> instead, to make that impossible.
/// </para>
/// </remarks>
public static class GhostBitStreamExtensions
{
    /// <summary>
    /// The largest bit width these helpers accept. <c>(1 &lt;&lt; 32)</c> would overflow a
    /// 32-bit shift in .NET (the shift count is taken mod 32), so widths are capped well below
    /// that — every documented use of these primitives is 20 bits or fewer.
    /// </summary>
    private const int MaxWidth = 31;

    private static uint MaxUnsignedValue(int bits)
    {
        if (bits is < 1 or > MaxWidth)
            throw new ArgumentOutOfRangeException(nameof(bits), bits, $"expected 1..{MaxWidth}");
        return (1u << bits) - 1u;
    }

    // ---------------------------------------------------------------- writeFloat (0x0043bf80)

    /// <summary>
    /// <c>writeFloat(F32, n)</c> — a value assumed to lie in <c>[0, 1]</c>, quantised to
    /// <paramref name="bits"/> bits: <c>WriteInt(round(value * ((1&lt;&lt;bits)-1)), bits)</c>.
    /// Values outside <c>[0, 1]</c> are clamped rather than wrapped, since the recovered formula
    /// gives no defined behaviour for out-of-range input.
    /// </summary>
    public static void WriteFloat(this BitStream stream, float value, int bits)
    {
        var max = MaxUnsignedValue(bits);
        var clamped = Math.Clamp(value, 0f, 1f);
        stream.WriteInt((uint)MathF.Round(clamped * max), bits);
    }

    /// <summary>Inverse of <see cref="WriteFloat"/>: <c>ReadInt(bits) / ((1&lt;&lt;bits)-1)</c>.</summary>
    public static float ReadFloat(this BitStream stream, int bits)
    {
        var max = MaxUnsignedValue(bits);
        return stream.ReadInt(bits) / (float)max;
    }

    // ---------------------------------------------------------------- writeSignedFloat (0x0043c000)

    /// <summary>
    /// <c>writeSignedFloat(F32, n)</c> — a value assumed to lie in <c>[-1, 1]</c>, remapped to
    /// <c>[0, 1]</c> and quantised: <c>WriteInt(round((value+1) * 0.5 * ((1&lt;&lt;bits)-1)), bits)</c>.
    /// </summary>
    public static void WriteSignedFloat(this BitStream stream, float value, int bits)
    {
        var max = MaxUnsignedValue(bits);
        var clamped = Math.Clamp(value, -1f, 1f);
        stream.WriteInt((uint)MathF.Round((clamped + 1f) * 0.5f * max), bits);
    }

    /// <summary>Inverse of <see cref="WriteSignedFloat"/>: <c>ReadInt(bits) / max * 2 - 1</c>.</summary>
    public static float ReadSignedFloat(this BitStream stream, int bits)
    {
        var max = MaxUnsignedValue(bits);
        return stream.ReadInt(bits) / (float)max * 2f - 1f;
    }

    // ---------------------------------------------------------------- writeSignedInt (0x0043c0a0)

    /// <summary>
    /// <c>writeSignedInt(S32, n)</c> — sign-and-magnitude, not two's complement: a sign flag
    /// (<c>true</c> for negative), then <c>abs(value)</c> in <c>bits-1</c> bits. See the type
    /// remarks for why this is not named <c>WriteSignedInt</c>.
    /// </summary>
    /// <remarks>
    /// Sign-and-magnitude has two representations of zero (+0 and -0); this writer always emits
    /// a positive sign for zero, and a reader that receives a "negative zero" simply gets 0 back
    /// — there is no information loss for round-tripping, only for distinguishing the sign bit
    /// of an exact-zero magnitude, which sign-and-magnitude formats never round-trip anyway.
    /// </remarks>
    public static void WriteGhostSignedInt(this BitStream stream, int value, int bits)
    {
        if (bits is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "expected 1..32");
        stream.WriteFlag(value < 0);
        var magnitude = value == int.MinValue ? (uint)int.MaxValue + 1u : (uint)Math.Abs(value);
        stream.WriteInt(magnitude, bits - 1);
    }

    /// <summary>Inverse of <see cref="WriteGhostSignedInt"/>.</summary>
    public static int ReadGhostSignedInt(this BitStream stream, int bits)
    {
        if (bits is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "expected 1..32");
        var negative = stream.ReadFlag();
        var magnitude = stream.ReadInt(bits - 1);
        return negative ? -(int)magnitude : (int)magnitude;
    }

    // ---------------------------------------------------------------- writeNormalVector (0x0043c170)

    /// <summary>
    /// <c>writeNormalVector(Point3F, b)</c> — encodes a direction as two angles, azimuth
    /// (<c>atan2(y, x)</c>) and inclination (<c>acos(z)</c>), each normalised to <c>[-1, 1]</c>
    /// by dividing by pi and sent through <see cref="WriteSignedFloat"/> at <paramref name="bits"/>
    /// bits. Total width is <c>2*bits + 1</c> per the recovered primitives table; the leading bit
    /// is modelled here as a validity flag guarding the degenerate zero-length input (where
    /// atan2/acos are not meaningful) — <b>the document does not name this bit's purpose</b>, and
    /// this is an assumption, not a recovered fact, made only to account for the documented width.
    /// </summary>
    public static void WriteNormalVector(this BitStream stream, Vector3 vector, int bits)
    {
        var lengthSquared = vector.LengthSquared();
        var valid = lengthSquared > 1e-12f;
        stream.WriteFlag(valid);

        var azimuth = 0f;
        var inclination = 0f;
        if (valid)
        {
            var n = vector / MathF.Sqrt(lengthSquared);
            azimuth = MathF.Atan2(n.Y, n.X) / MathF.PI;
            inclination = MathF.Acos(Math.Clamp(n.Z, -1f, 1f)) / MathF.PI;
        }

        stream.WriteSignedFloat(azimuth, bits);
        stream.WriteSignedFloat(inclination, bits);
    }

    /// <summary>Inverse of <see cref="WriteNormalVector"/>. Returns <see cref="Vector3.Zero"/> for a vector written as invalid.</summary>
    public static Vector3 ReadNormalVector(this BitStream stream, int bits)
    {
        var valid = stream.ReadFlag();
        var azimuth = stream.ReadSignedFloat(bits) * MathF.PI;
        var inclination = stream.ReadSignedFloat(bits) * MathF.PI;
        if (!valid) return Vector3.Zero;

        var sinInclination = MathF.Sin(inclination);
        return new Vector3(
            MathF.Cos(azimuth) * sinInclination,
            MathF.Sin(azimuth) * sinInclination,
            MathF.Cos(inclination));
    }

    // ---------------------------------------------------------------- writeDataBlockId (0x00436ce0)

    /// <summary>
    /// <c>writeDataBlockId</c> — a presence flag followed by the 11-bit id when present.
    /// Modelled as a nullable id: <c>null</c> means "no datablock", matching the -1-sentinel
    /// convention the rest of this format uses (e.g. <c>GameBase +0x270</c>).
    /// </summary>
    public static void WriteDataBlockId(this BitStream stream, int? id)
    {
        var present = id.HasValue;
        stream.WriteFlag(present);
        if (present) stream.WriteInt((uint)id!.Value, 11);
    }

    /// <summary>Inverse of <see cref="WriteDataBlockId"/>.</summary>
    public static int? ReadDataBlockId(this BitStream stream) =>
        stream.ReadFlag() ? (int)stream.ReadInt(11) : null;

    // ---------------------------------------------------------------- raw 32-bit float

    /// <summary>
    /// A raw, unquantised IEEE-754 float written as 32 bits (LSB-first, per <see cref="BitStream"/>'s
    /// bit order). Not itself a named primitive in the recovered table, but required by two blocks
    /// that the document describes only as writing "raw 32-bit floats": the invincible block and
    /// the level-3 (absolute) fallback of <c>writeCompressedPoint</c>.
    /// </summary>
    public static void WriteRawFloat(this BitStream stream, float value) =>
        stream.WriteInt(BitConverter.SingleToUInt32Bits(value), 32);

    /// <summary>Inverse of <see cref="WriteRawFloat"/>.</summary>
    public static float ReadRawFloat(this BitStream stream) =>
        BitConverter.UInt32BitsToSingle(stream.ReadInt(32));
}
