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

    /// <summary>Total header size in bits.</summary>
    public const int HeaderBits = FlagBits + SequenceBits * 2;

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
