namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// The per-packet connection header that precedes every in-session game packet.
/// </summary>
/// <remarks>
/// <para>
/// Layout recovered from a live capture of the stock client talking to the reference server
/// (149 s, 7,722 packets), not from documentation:
/// </para>
/// <code>
///   bits [0:2]    flags
///   bits [2:11]   send sequence  (9 bits, wraps at 512)
///   bits [11:20]  ack sequence   (9 bits, the peer's send sequence being acknowledged)
/// </code>
/// <para>
/// Two independent lines of evidence fix this. First, decoded least-significant-bit-first,
/// the send field advances by exactly one on 99.7% of consecutive packets in both directions
/// — the remainder being the retransmits and reordering any UDP flow shows. Second, and more
/// conclusively, each side's ack field tracks the <em>other</em> side's send counter, so the
/// two flows corroborate one another rather than merely fitting a curve.
/// </para>
/// <para>
/// This is also what validates <see cref="BitStream"/>'s bit order: read most-significant-bit
/// first, the same bytes decode to noise instead of a monotonic counter.
/// </para>
/// </remarks>
public readonly record struct PacketHeader(uint Flags, uint SendSequence, uint AckSequence)
{
    public const int FlagBits = 2;
    public const int SequenceBits = 9;

    /// <summary>
    /// Bits consumed by the fields this type models: the two leading flag bits and the two
    /// sequence numbers.
    /// </summary>
    /// <remarks>
    /// <b>This is not the whole header.</b> See <see cref="FullHeaderBits"/> — five further
    /// fixed bits and a variable-length ack mask follow, recovered later from
    /// <c>buildSendPacketHeader</c> (<c>0x0043d2d0</c>) and its read side (<c>0x0043d4d0</c>).
    /// This constant is retained because it is exactly what is needed to reach the two sequence
    /// numbers, which is all several callers want.
    /// </remarks>
    public const int HeaderBits = FlagBits + SequenceBits * 2;

    /// <summary>Bits in the fixed part of the real header, before the variable ack mask.</summary>
    /// <remarks>
    /// The complete layout, confirmed against both the disassembly and the captured traffic:
    /// <code>
    ///   bit  0      constant 1 — marks a sequenced packet; 0 marks out-of-band control
    ///   bit  1      connect-sequence parity; the receiver drops the packet on a mismatch
    ///   bits 2..10  send sequence  (9 bits)
    ///   bits 11..19 ack sequence   (9 bits)
    ///   bits 20..21 packet type    (2 bits; the receiver rejects a value above 2)
    ///   bits 22..24 ack byte count (3 bits; the receiver rejects a value above 4)
    ///   then        8 * ackByteCount bits of ack mask
    /// </code>
    /// Only packet type 0 carries a body. Bit 0 is also what
    /// <see cref="ControlPacket.IsControl"/> tests, which is why that classification agrees with
    /// this layout rather than being an independent heuristic.
    /// </remarks>
    public const int FullHeaderBits = 25;

    /// <summary>Bits of packet-type field (values above 2 are rejected by the receiver).</summary>
    public const int PacketTypeBits = 2;

    /// <summary>Bits of ack-byte-count field (values above 4 are rejected by the receiver).</summary>
    public const int AckByteCountBits = 3;

    /// <summary>Sequence numbers are modulo this; both fields wrap.</summary>
    public const uint SequenceModulo = 1u << SequenceBits;

    public static PacketHeader Read(BitStream stream)
    {
        var flags = stream.ReadInt(FlagBits);
        var send = stream.ReadInt(SequenceBits);
        var ack = stream.ReadInt(SequenceBits);
        return new PacketHeader(flags, send, ack);
    }

    public void Write(BitStream stream)
    {
        stream.WriteInt(Flags, FlagBits);
        stream.WriteInt(SendSequence, SequenceBits);
        stream.WriteInt(AckSequence, SequenceBits);
    }

    /// <summary>Parses a header from the front of a datagram payload.</summary>
    public static PacketHeader Parse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length * 8 < HeaderBits)
            throw new ArgumentException($"datagram too short for a {HeaderBits}-bit header", nameof(datagram));
        return Read(new BitStream(datagram.ToArray()));
    }

    /// <summary>
    /// Forward distance from <paramref name="from"/> to this packet's send sequence, honouring
    /// the 9-bit wrap. Used to spot gaps (loss) and duplicates in a flow.
    /// </summary>
    public uint DistanceFrom(uint from) => (SendSequence - from) % SequenceModulo;

    public override string ToString() =>
        $"flags={Flags} send={SendSequence} ack={AckSequence}";
}
