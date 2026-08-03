using System.Numerics;

namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>Bit 3 payload: <c>writeFloat(damage/maxDamage, 6)</c>, <c>writeInt(damageState, 2)</c>, an unidentified flag, then an 8-bit normal vector (17 bits).</summary>
public struct DamageGhostFields
{
    /// <summary><c>damage / maxDamage</c>, quantised to 6 bits.</summary>
    public float DamageFraction;

    /// <summary>2-bit damage state enum; the document does not name the values.</summary>
    public uint DamageState;

    /// <summary>
    /// A flag written between <c>damageState</c> and the normal vector. Its purpose is not
    /// identified in <c>GhostProtocol.md</c> — the document lists it only as "writeFlag" with no
    /// further detail, distinct from the normal vector's own internal validity flag (already
    /// accounted for in the 17-bit width of <see cref="DamageDirection"/>'s encoding).
    /// </summary>
    public bool UnidentifiedFlag;

    /// <summary>8-bit-per-angle normal vector (17 bits total) — presumed damage/impact direction, not confirmed by the document.</summary>
    public Vector3 DamageDirection;
}

/// <summary>Bits 9-12 payload, one per sound thread: a play flag, then a datablock id.</summary>
public struct SoundThreadGhostFields
{
    public bool Play;
    public int? ProfileDataBlockId;
}

/// <summary>Bits 13-16 payload, one per animation thread: sequence, state, and two flags.</summary>
public struct AnimThreadGhostFields
{
    /// <summary>5-bit animation sequence index.</summary>
    public uint Sequence;

    /// <summary>2-bit animation state.</summary>
    public uint State;

    /// <summary>Two flags following the sequence/state fields; the document does not name them.</summary>
    public bool Flag0;
    public bool Flag1;
}

/// <summary>
/// Bits 17-24 payload, one per mounted image slot: a datablock id, a string handle, five flags,
/// a 3-bit field, and (on the initial update only) one extra flag.
/// </summary>
/// <remarks>
/// <b>Known gap.</b> The document lists the string-handle field as
/// <c>NetConnection::packStringHandleU32</c> (<c>0x005887f0</c>) with "variable" width and no
/// recovered bit-level format — unlike every other primitive in the table, it was not decoded.
/// It is also not in the set of primitives this task asked to build. This type therefore does
/// not attempt to serialise a string handle at all: <see cref="Pack"/>/<see cref="Unpack"/> for
/// a mounted image are symmetric with each other (so this codec's own round trip is
/// self-consistent), but they are <b>not</b> wire-compatible with the stock client for this one
/// field until <c>packStringHandleU32</c> is separately recovered.
/// </remarks>
public struct MountedImageGhostFields
{
    public int? DataBlockId;

    /// <summary>Five flags following the datablock id / string handle; the document does not name them.</summary>
    public bool Flag0, Flag1, Flag2, Flag3, Flag4;

    /// <summary>3-bit field; the document names this only as <c>writeInt(.., 3)</c> — likely the mounted-image slot/mount point, not confirmed.</summary>
    public uint SlotField;

    /// <summary>The extra flag sent only when the enclosing update is the object's initial ghost.</summary>
    public bool InitialExtraFlag;
}

/// <summary>Bit 7 payload: a normal vector and an energy fraction.</summary>
public struct ShieldGhostFields
{
    /// <summary>8-bit-per-angle normal vector (17 bits) — presumed shield facing, not confirmed.</summary>
    public Vector3 ShieldNormal;

    /// <summary><c>energy</c>, quantised to 5 bits.</summary>
    public float Energy;

    // NOTE: GhostProtocol.md's "Unidentified" section calls out "ShapeBase +0x740 /
    // byte[+0x774] inside the shield block" as fields the recovery could not pin down — not
    // even their bit width is known, so there is nothing to model here. Anything past
    // ShieldNormal/Energy on the real wire is missing from this type until that gap is closed.
}

/// <summary>Bit 8 payload: two raw (unquantised) 32-bit floats.</summary>
public struct InvincibleGhostFields
{
    public float A;
    public float B;
}

/// <summary>Bit 5 payload ("mount tail"): the ghost index and node of the object this one is mounted to.</summary>
public struct MountGhostFields
{
    /// <summary>10-bit ghost index (matches the format-wide "ghost ids are 10 bits" rule).</summary>
    public uint GhostIndex;

    /// <summary>5-bit mount node.</summary>
    public uint Node;
}

/// <summary>
/// <c>ShapeBase::packUpdate</c> (<c>0x5eead0</c>) / <c>unpackUpdate</c> (<c>0x5ef0e0</c>).
/// Writes <see cref="GameBaseGhostState"/>'s fields first (parent bits before child bits, per the
/// document), then its own group of fields, gated as a whole by a single flag on
/// <see cref="GroupGateMask"/> before any individual field is considered.
/// </summary>
/// <remarks>
/// Field order below follows <c>GhostProtocol.md</c>'s "Field layout" section prose exactly:
/// damage, sound threads, anim threads, mounted images, cloak/shield/invincible, then the mount
/// tail last — note this is <b>not</b> mask-bit numeric order (mount is bit 5, ahead of cloak's
/// bit 6, but its "mount tail" content is written after all of them). That ordering is taken
/// from the recovered code flow, not inferred from the bit numbering.
/// </remarks>
public class ShapeBaseGhostState : GameBaseGhostState
{
    /// <summary>
    /// <c>0x01FFFFE8</c> — the single flag <c>ShapeBase::packUpdate</c> gates its entire section
    /// behind. Exactly bits <c>{3} &#x222A; {5..24}</c>, which the document verifies against the
    /// union of the sub-blocks below ("self-validating").
    /// </summary>
    public const uint GroupGateMask = 0x01FFFFE8u;

    public DamageGhostFields Damage;
    public readonly SoundThreadGhostFields[] SoundThreads = new SoundThreadGhostFields[4];
    public readonly AnimThreadGhostFields[] AnimThreads = new AnimThreadGhostFields[4];
    public readonly MountedImageGhostFields[] MountedImages = new MountedImageGhostFields[8];
    public bool Cloaked;
    public ShieldGhostFields Shield;
    public InvincibleGhostFields Invincible;
    public MountGhostFields Mount;

    /// <param name="isInitial">Whether this is the object's initial ghost to this connection — gates the extra per-mounted-image flag.</param>
    public virtual void Pack(BitStream stream, GhostMaskBits mask, bool isInitial)
    {
        base.Pack(stream, mask);

        var hasAny = ((uint)mask & GroupGateMask) != 0;
        stream.WriteFlag(hasAny);
        if (!hasAny) return;

        var hasDamage = (mask & GhostMaskBits.Damage) != 0;
        stream.WriteFlag(hasDamage);
        if (hasDamage)
        {
            stream.WriteFloat(Damage.DamageFraction, 6);
            stream.WriteInt(Damage.DamageState, 2);
            stream.WriteFlag(Damage.UnidentifiedFlag);
            stream.WriteNormalVector(Damage.DamageDirection, 8);
        }

        for (var i = 0; i < 4; i++)
        {
            var gate = (mask & GhostMaskCatalog.SoundThread(i)) != 0;
            stream.WriteFlag(gate);
            if (!gate) continue;
            ref readonly var t = ref SoundThreads[i];
            stream.WriteFlag(t.Play);
            stream.WriteDataBlockId(t.ProfileDataBlockId);
        }

        for (var i = 0; i < 4; i++)
        {
            var gate = (mask & GhostMaskCatalog.AnimThread(i)) != 0;
            stream.WriteFlag(gate);
            if (!gate) continue;
            ref readonly var t = ref AnimThreads[i];
            stream.WriteInt(t.Sequence, 5);
            stream.WriteInt(t.State, 2);
            stream.WriteFlag(t.Flag0);
            stream.WriteFlag(t.Flag1);
        }

        for (var i = 0; i < 8; i++)
        {
            var gate = (mask & GhostMaskCatalog.MountedImage(i)) != 0;
            stream.WriteFlag(gate);
            if (!gate) continue;
            ref readonly var m = ref MountedImages[i];
            stream.WriteDataBlockId(m.DataBlockId);
            // string handle: see MountedImageGhostFields remarks — not serialised, known gap.
            stream.WriteFlag(m.Flag0);
            stream.WriteFlag(m.Flag1);
            stream.WriteFlag(m.Flag2);
            stream.WriteFlag(m.Flag3);
            stream.WriteFlag(m.Flag4);
            stream.WriteInt(m.SlotField, 3);
            if (isInitial) stream.WriteFlag(m.InitialExtraFlag);
        }

        // Unlike sound/anim/mounted-image/shield/invincible, the document lists no payload for
        // the cloak block beyond "each behind its own flag" — so this is modelled as exactly
        // that one bit, mask-gated the same way as every other block, and carrying no further
        // content. Whether that single bit is itself the on/off value, or merely a "cloak
        // changed" trigger with the value living elsewhere, is not stated; Unpack takes the
        // conservative reading (the bit is the value) since that is the only information the
        // wire actually carries here.
        stream.WriteFlag((mask & GhostMaskBits.Cloak) != 0);

        var hasShield = (mask & GhostMaskBits.Shield) != 0;
        stream.WriteFlag(hasShield);
        if (hasShield)
        {
            stream.WriteNormalVector(Shield.ShieldNormal, 8);
            stream.WriteFloat(Shield.Energy, 5);
        }

        var hasInvincible = (mask & GhostMaskBits.Invincible) != 0;
        stream.WriteFlag(hasInvincible);
        if (hasInvincible)
        {
            stream.WriteRawFloat(Invincible.A);
            stream.WriteRawFloat(Invincible.B);
        }

        var hasMount = (mask & GhostMaskBits.Mounted) != 0;
        stream.WriteFlag(hasMount);
        if (hasMount)
        {
            stream.WriteInt(Mount.GhostIndex, 10);
            stream.WriteInt(Mount.Node, 5);
        }
    }

    /// <summary>Inverse of <see cref="Pack"/>. No <c>mask</c> parameter — see <see cref="GameBaseGhostState.Unpack"/>.</summary>
    public virtual void Unpack(BitStream stream, bool isInitial)
    {
        base.Unpack(stream);

        var hasAny = stream.ReadFlag();
        if (!hasAny) return;

        if (stream.ReadFlag())
        {
            Damage.DamageFraction = stream.ReadFloat(6);
            Damage.DamageState = stream.ReadInt(2);
            Damage.UnidentifiedFlag = stream.ReadFlag();
            Damage.DamageDirection = stream.ReadNormalVector(8);
        }

        for (var i = 0; i < 4; i++)
        {
            if (!stream.ReadFlag()) continue;
            SoundThreads[i].Play = stream.ReadFlag();
            SoundThreads[i].ProfileDataBlockId = stream.ReadDataBlockId();
        }

        for (var i = 0; i < 4; i++)
        {
            if (!stream.ReadFlag()) continue;
            AnimThreads[i].Sequence = stream.ReadInt(5);
            AnimThreads[i].State = stream.ReadInt(2);
            AnimThreads[i].Flag0 = stream.ReadFlag();
            AnimThreads[i].Flag1 = stream.ReadFlag();
        }

        for (var i = 0; i < 8; i++)
        {
            if (!stream.ReadFlag()) continue;
            MountedImages[i].DataBlockId = stream.ReadDataBlockId();
            MountedImages[i].Flag0 = stream.ReadFlag();
            MountedImages[i].Flag1 = stream.ReadFlag();
            MountedImages[i].Flag2 = stream.ReadFlag();
            MountedImages[i].Flag3 = stream.ReadFlag();
            MountedImages[i].Flag4 = stream.ReadFlag();
            MountedImages[i].SlotField = stream.ReadInt(3);
            if (isInitial) MountedImages[i].InitialExtraFlag = stream.ReadFlag();
        }

        Cloaked = stream.ReadFlag();

        if (stream.ReadFlag())
        {
            Shield.ShieldNormal = stream.ReadNormalVector(8);
            Shield.Energy = stream.ReadFloat(5);
        }

        if (stream.ReadFlag())
        {
            Invincible.A = stream.ReadRawFloat();
            Invincible.B = stream.ReadRawFloat();
        }

        if (stream.ReadFlag())
        {
            Mount.GhostIndex = stream.ReadInt(10);
            Mount.Node = stream.ReadInt(5);
        }
    }
}
