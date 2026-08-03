namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>
/// A single ghost record's framing header, as read off the front of a record inside the ghost
/// section — everything up to (but not including) the object's own <c>packUpdate</c>/<c>unpackUpdate</c> content.
/// </summary>
/// <param name="GhostIndex">The 10-bit-capped ghost id (<c>getGhostIndex</c> masks with <c>0x3ff</c>), written in <paramref name="IdBits"/> bits.</param>
/// <param name="IdBits">The width <see cref="GhostIndex"/> was written/read at for this section — fixed per section by <see cref="GhostRecordFraming.ReadSectionHeader"/>.</param>
/// <param name="IsRemove">True if this record removes the ghost rather than updating it.</param>
/// <param name="ClassId">The 7-bit class id, present only the first time this ghost id is sent to this connection.</param>
public readonly record struct GhostRecordHeader(uint GhostIndex, int IdBits, bool IsRemove, int? ClassId);

/// <summary>
/// <c>NetConnection::writePacket</c> (<c>0x587b90</c>) → <c>eventWritePacket</c> (<c>0x583540</c>)
/// → <c>ghostWritePacket</c> (<c>0x583db0</c>): the section of a data packet that carries ghost
/// (object replication) records.
/// </summary>
/// <remarks>
/// <para>
/// Per <c>GhostProtocol.md</c>'s "Not verified against captured packets" section, this framing
/// has <b>not</b> been cross-checked against real traffic — reaching it requires decoding roughly
/// thirty undocumented fields upstream in <c>GameConnection::writePacket</c> first. It is,
/// however, exactly what the document recovers from <c>ghostWritePacket</c>/its unpack
/// counterpart, and the framing is self-terminating and length-implicit (no record count is ever
/// written — the section just runs until a <c>false</c> continuation flag), so it composes
/// correctly with the field-layout types in this namespace regardless of what precedes it in a
/// real packet.
/// </para>
/// </remarks>
public static class GhostRecordFraming
{
    /// <summary>
    /// Writes the section header: <c>writeFlag(hasGhosts)</c>, then — only if true —
    /// <c>writeInt(idSize - 3, 3)</c>. <paramref name="idBits"/> must be in <c>[3, 10]</c>: the
    /// minimum the 3-bit encoding can express, and the maximum per "ghost ids are 10 bits".
    /// </summary>
    public static void WriteSectionHeader(BitStream stream, bool hasGhosts, int idBits)
    {
        stream.WriteFlag(hasGhosts);
        if (!hasGhosts) return;
        if (idBits is < 3 or > 10)
            throw new ArgumentOutOfRangeException(nameof(idBits), idBits, "expected 3..10 (ghost ids are 10 bits)");
        stream.WriteInt((uint)(idBits - 3), 3);
    }

    /// <summary>
    /// Reads the section header. Returns <c>null</c> if the section is empty
    /// (<c>hasGhosts</c> was false), otherwise the id width every subsequent record in the
    /// section is written/read at.
    /// </summary>
    public static int? ReadSectionHeader(BitStream stream)
    {
        if (!stream.ReadFlag()) return null;
        return (int)stream.ReadInt(3) + 3;
    }

    /// <summary>
    /// Writes one record's framing: the continuation flag (always <c>true</c> — the terminator is
    /// written separately by <see cref="WriteTerminator"/>), the ghost index, the remove flag,
    /// and — when <paramref name="header"/> carries one — the class id. Does not write the
    /// object's own <c>packUpdate</c> content; the caller does that immediately after, only when
    /// <c>!IsRemove</c>.
    /// </summary>
    public static void WriteRecordHeader(BitStream stream, GhostRecordHeader header)
    {
        stream.WriteFlag(true);
        stream.WriteInt(header.GhostIndex, header.IdBits);
        stream.WriteFlag(header.IsRemove);
        if (!header.IsRemove && header.ClassId is { } classId)
            stream.WriteInt((uint)classId, 7);
    }

    /// <summary>
    /// Reads one record's framing, or returns <c>null</c> if the continuation flag was false
    /// (end of section — the caller should stop, matching <see cref="WriteTerminator"/>).
    /// <paramref name="expectClassId"/> must reflect whether the reader already knows this ghost
    /// id (mirroring the writer's own "first time sent" bookkeeping) — that bookkeeping is
    /// per-connection state outside this type's scope, exactly like <c>isInitial</c> elsewhere in
    /// this namespace.
    /// </summary>
    public static GhostRecordHeader? ReadRecordHeader(BitStream stream, int idBits, bool expectClassId)
    {
        if (!stream.ReadFlag()) return null;
        var ghostIndex = stream.ReadInt(idBits);
        var isRemove = stream.ReadFlag();
        int? classId = !isRemove && expectClassId ? (int)stream.ReadInt(7) : null;
        return new GhostRecordHeader(ghostIndex, idBits, isRemove, classId);
    }

    /// <summary>Writes the section terminator: <c>writeFlag(false)</c>.</summary>
    public static void WriteTerminator(BitStream stream) => stream.WriteFlag(false);
}
