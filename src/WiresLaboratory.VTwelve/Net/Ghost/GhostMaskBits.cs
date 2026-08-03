namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>
/// The ghost update mask bit assignment recovered in <c>GhostProtocol.md</c> ("Mask bits" table).
/// Each bit gates one field group in the <c>Player -&gt; ShapeBase -&gt; GameBase</c> pack chain;
/// a ghost update only carries the groups whose bit is set in the mask passed to <c>packUpdate</c>.
/// </summary>
/// <remarks>
/// <para>
/// The document's own cross-check is what makes this table trustworthy rather than merely
/// plausible: <c>ShapeBase::packUpdate</c>'s single group-gate flag is <c>writeFlag(mask &amp;
/// 0x01FFFFE8)</c>, and <c>0x01FFFFE8</c> is exactly the union of bits <c>{3}</c> and
/// <c>{5..24}</c> — precisely the bits the function's own sub-blocks then consume, and precisely
/// excluding bits 0-2 (NetObject/GameBase) and bit 4 (Player). See
/// <see cref="ShapeBaseGhostState.GroupGateMask"/>.
/// </para>
/// <para>
/// Bit 27's meaning is not identified in the document; it is modelled here only as a bit position
/// (<see cref="PlayerUnknownField27"/>) with a 3-bit payload, per the recovered wire shape — no
/// semantics are claimed for it.
/// </para>
/// </remarks>
[Flags]
public enum GhostMaskBits : uint
{
    None = 0,

    /// <summary>Bit 0 — NetObject: initial update (first ghost of this object sent to this connection).</summary>
    Initial = 1u << 0,

    /// <summary>Bit 1 — GameBase: datablock.</summary>
    DataBlock = 1u << 1,

    /// <summary>Bit 2 — GameBase: target id / extended info (<c>GameBase +0x270</c>, inferred to be a target id).</summary>
    Target = 1u << 2,

    /// <summary>Bit 3 — ShapeBase: damage.</summary>
    Damage = 1u << 3,

    /// <summary>Bit 4 — Player: no-warp.</summary>
    NoWarp = 1u << 4,

    /// <summary>Bit 5 — ShapeBase: mounted (to another object).</summary>
    Mounted = 1u << 5,

    /// <summary>Bit 6 — ShapeBase: cloak.</summary>
    Cloak = 1u << 6,

    /// <summary>Bit 7 — ShapeBase: shield.</summary>
    Shield = 1u << 7,

    /// <summary>Bit 8 — ShapeBase: invincible.</summary>
    Invincible = 1u << 8,

    /// <summary>Bits 9-12 — ShapeBase: sound thread 0.</summary>
    SoundThread0 = 1u << 9,
    /// <summary>Bits 9-12 — ShapeBase: sound thread 1.</summary>
    SoundThread1 = 1u << 10,
    /// <summary>Bits 9-12 — ShapeBase: sound thread 2.</summary>
    SoundThread2 = 1u << 11,
    /// <summary>Bits 9-12 — ShapeBase: sound thread 3.</summary>
    SoundThread3 = 1u << 12,

    /// <summary>Bits 13-16 — ShapeBase: animation thread 0.</summary>
    AnimThread0 = 1u << 13,
    /// <summary>Bits 13-16 — ShapeBase: animation thread 1.</summary>
    AnimThread1 = 1u << 14,
    /// <summary>Bits 13-16 — ShapeBase: animation thread 2.</summary>
    AnimThread2 = 1u << 15,
    /// <summary>Bits 13-16 — ShapeBase: animation thread 3.</summary>
    AnimThread3 = 1u << 16,

    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 0.</summary>
    MountedImage0 = 1u << 17,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 1.</summary>
    MountedImage1 = 1u << 18,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 2.</summary>
    MountedImage2 = 1u << 19,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 3.</summary>
    MountedImage3 = 1u << 20,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 4.</summary>
    MountedImage4 = 1u << 21,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 5.</summary>
    MountedImage5 = 1u << 22,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 6.</summary>
    MountedImage6 = 1u << 23,
    /// <summary>Bits 17-24 — ShapeBase: mounted image slot 7.</summary>
    MountedImage7 = 1u << 24,

    /// <summary>Bit 25 — Player: action animation.</summary>
    PlayerActionAnimation = 1u << 25,

    /// <summary>Bit 26 — Player: move + position.</summary>
    PlayerMoveAndPosition = 1u << 26,

    /// <summary>Bit 27 — Player: 3-bit field, meaning not identified in the source document.</summary>
    PlayerUnknownField27 = 1u << 27,
}

/// <summary>Index-based lookups into the four sound-thread, four anim-thread and eight mounted-image mask bits.</summary>
public static class GhostMaskCatalog
{
    /// <summary>The mask bit for sound thread <paramref name="index"/> (0-3), bits 9-12.</summary>
    public static GhostMaskBits SoundThread(int index)
    {
        if (index is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(index), index, "expected 0..3");
        return (GhostMaskBits)(1u << (9 + index));
    }

    /// <summary>The mask bit for animation thread <paramref name="index"/> (0-3), bits 13-16.</summary>
    public static GhostMaskBits AnimThread(int index)
    {
        if (index is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(index), index, "expected 0..3");
        return (GhostMaskBits)(1u << (13 + index));
    }

    /// <summary>The mask bit for mounted-image slot <paramref name="index"/> (0-7), bits 17-24.</summary>
    public static GhostMaskBits MountedImage(int index)
    {
        if (index is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(index), index, "expected 0..7");
        return (GhostMaskBits)(1u << (17 + index));
    }
}
