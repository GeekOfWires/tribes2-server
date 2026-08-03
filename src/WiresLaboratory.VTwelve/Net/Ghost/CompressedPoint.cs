using System.Numerics;

namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>
/// The four precision levels <c>NetConnection::writeCompressedPoint</c> (<c>0x00588ac0</c>)
/// chooses between, based on how far the point is from the connection's scope position.
/// </summary>
public enum CompressedPointLevel : byte
{
    /// <summary>Delta magnitude &lt; 32768 scale units: 16-bit signed components.</summary>
    Delta16 = 0,

    /// <summary>Delta magnitude &lt; 131072 scale units: 18-bit signed components.</summary>
    Delta18 = 1,

    /// <summary>Delta magnitude &lt; 524288 scale units: 20-bit signed components.</summary>
    Delta20 = 2,

    /// <summary>No usable scope position, or the delta is too large to quantise: three raw 32-bit floats of the absolute position.</summary>
    Absolute = 3,
}

/// <summary>
/// <c>writeCompressedPoint</c> / <c>0x00588cf0</c> (read side): a position sent as a quantised
/// delta from the connection's current scope position when that delta is small enough, falling
/// back to an absolute, unquantised position otherwise. Always called with <c>scale = 0.01</c> by
/// the ghost format (a 1cm quantisation step for the delta-encoded levels).
/// </summary>
public static class CompressedPointCodec
{
    /// <summary>The scale every documented caller of <c>writeCompressedPoint</c> uses.</summary>
    public const float DefaultScale = 0.01f;

    /// <summary>
    /// Writes <paramref name="position"/>, delta-encoded against <paramref name="scopePosition"/>
    /// when one is available and close enough, else as an absolute position (level 3).
    /// </summary>
    public static void WriteCompressedPoint(this BitStream stream, Vector3 position, Vector3? scopePosition, float scale = DefaultScale)
    {
        var level = CompressedPointLevel.Absolute;
        var delta = Vector3.Zero;

        if (scopePosition is { } scope)
        {
            delta = position - scope;
            var magnitude = delta.Length() / scale;
            level = magnitude switch
            {
                < 32768f => CompressedPointLevel.Delta16,
                < 131072f => CompressedPointLevel.Delta18,
                < 524288f => CompressedPointLevel.Delta20,
                _ => CompressedPointLevel.Absolute,
            };
        }

        stream.WriteInt((uint)level, 2);
        if (level == CompressedPointLevel.Absolute)
        {
            stream.WriteRawFloat(position.X);
            stream.WriteRawFloat(position.Y);
            stream.WriteRawFloat(position.Z);
            return;
        }

        var bits = BitsFor(level);
        stream.WriteGhostSignedInt((int)MathF.Round(delta.X / scale), bits);
        stream.WriteGhostSignedInt((int)MathF.Round(delta.Y / scale), bits);
        stream.WriteGhostSignedInt((int)MathF.Round(delta.Z / scale), bits);
    }

    /// <summary>
    /// Inverse of <see cref="WriteCompressedPoint"/>. <paramref name="scopePosition"/> must be
    /// the same value the writer used — the delta-encoded levels are meaningless without it.
    /// </summary>
    public static Vector3 ReadCompressedPoint(this BitStream stream, Vector3? scopePosition, float scale = DefaultScale)
    {
        var level = (CompressedPointLevel)stream.ReadInt(2);
        if (level == CompressedPointLevel.Absolute)
        {
            var x = stream.ReadRawFloat();
            var y = stream.ReadRawFloat();
            var z = stream.ReadRawFloat();
            return new Vector3(x, y, z);
        }

        var bits = BitsFor(level);
        var dx = stream.ReadGhostSignedInt(bits) * scale;
        var dy = stream.ReadGhostSignedInt(bits) * scale;
        var dz = stream.ReadGhostSignedInt(bits) * scale;

        // A delta-encoded level with no scope position is a caller error (the writer could not
        // have produced one either), but degrade to origin-relative rather than throwing, so a
        // decode never crashes on a malformed/adversarial packet.
        var scope = scopePosition ?? Vector3.Zero;
        return scope + new Vector3(dx, dy, dz);
    }

    private static int BitsFor(CompressedPointLevel level) => level switch
    {
        CompressedPointLevel.Delta16 => 16,
        CompressedPointLevel.Delta18 => 18,
        CompressedPointLevel.Delta20 => 20,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "no bit width for this level"),
    };
}
