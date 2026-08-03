namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>
/// <c>GameBase::packUpdate</c> (<c>0x5e32d0</c>) / <c>unpackUpdate</c> (<c>0x5e3360</c>) —
/// the root of the ghost pack chain. Per <c>GhostProtocol.md</c>, <c>NetObject</c> and
/// <c>SceneObject</c> contribute no bits of their own, so <c>GameBase</c> is where the wire
/// format actually starts; <see cref="Net.Ghost.ShapeBaseGhostState"/> and
/// <see cref="Net.Ghost.PlayerGhostState"/> write this class's fields first, then their own.
/// </summary>
public class GameBaseGhostState
{
    /// <summary>Bit 1: the object's datablock id, or <c>null</c> if it has none / the bit is not set.</summary>
    public int? DataBlockId { get; set; }

    /// <summary>
    /// Bit 2: a 9-bit id with a -1 sentinel, at <c>GameBase +0x270</c>. <b>Inferred</b> (not
    /// proven) to be a target id, from neighbouring event classes — modelled here only as an
    /// optional 9-bit value, per the document's own caveat.
    /// </summary>
    public int? TargetId { get; set; }

    /// <summary>
    /// Writes this object's contribution to a ghost update. Field order:
    /// <list type="number">
    /// <item><c>writeFlag((mask &amp; DataBlock) &amp;&amp; DataBlockId.HasValue)</c>, then the 11-bit id if the flag was true.</item>
    /// <item><c>writeFlag(mask &amp; Target)</c>, then <c>writeFlag(TargetId != null)</c>, then the 9-bit id if that second flag was true.</item>
    /// </list>
    /// </summary>
    public virtual void Pack(BitStream stream, GhostMaskBits mask)
    {
        var hasDataBlock = (mask & GhostMaskBits.DataBlock) != 0 && DataBlockId.HasValue;
        stream.WriteFlag(hasDataBlock);
        if (hasDataBlock) stream.WriteInt((uint)DataBlockId!.Value, 11);

        var targetGate = (mask & GhostMaskBits.Target) != 0;
        stream.WriteFlag(targetGate);
        if (targetGate)
        {
            var hasTarget = TargetId.HasValue;
            stream.WriteFlag(hasTarget);
            if (hasTarget) stream.WriteInt((uint)TargetId!.Value, 9);
        }
    }

    /// <summary>
    /// Inverse of <see cref="Pack"/>. Fields not present on the wire are left unchanged (delta
    /// semantics: absence means "unchanged", not "cleared"). Deliberately does not take a
    /// <c>mask</c> parameter: the mask is a write-side bookkeeping value only — it is never sent
    /// on the wire (see <c>GhostRecordFraming</c>'s framing, which packs field data but no raw
    /// mask word) — so a decoder must, and can, tell what is present purely from the gate flags
    /// actually in the stream.
    /// </summary>
    public virtual void Unpack(BitStream stream)
    {
        if (stream.ReadFlag())
            DataBlockId = (int)stream.ReadInt(11);

        if (stream.ReadFlag())
        {
            TargetId = stream.ReadFlag() ? (int)stream.ReadInt(9) : null;
        }
    }
}
