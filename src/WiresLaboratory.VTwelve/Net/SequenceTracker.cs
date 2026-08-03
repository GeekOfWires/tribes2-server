namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// How a newly observed peer send-sequence number relates to what has been seen from that peer
/// so far.
/// </summary>
public enum SequenceEvent
{
    /// <summary>The very first packet observed from this peer — nothing to compare against yet.</summary>
    Initial,

    /// <summary>Send sequence advanced by exactly one: the expected, common case.</summary>
    InOrder,

    /// <summary>Send sequence jumped forward by more than one: one or more packets in between were lost or badly reordered.</summary>
    Gap,

    /// <summary>Send sequence did not advance, or is behind the highest seen so far (honouring the 9-bit wrap): a retransmit, a reordered late arrival, or a stale duplicate.</summary>
    Duplicate,
}

/// <summary>
/// Tracks one direction of a session's 9-bit wrapping send-sequence counter (see
/// <see cref="PacketHeader"/>), so a caller can build the ack field to send back and notice
/// loss/reordering/duplicates without special-casing the wrap at 512.
/// </summary>
/// <remarks>
/// <para>
/// The forward-distance arithmetic is the same modulo subtraction as
/// <see cref="PacketHeader.DistanceFrom"/>, which <c>PcapProtocolCheck</c> already validates
/// against live traffic (send sequence advances by exactly 1 on 99.7% of consecutive packets in
/// a real capture). What this type adds is classifying that distance — in-order vs. gap vs.
/// duplicate — using a standard "which half of the wrap window" split (mirroring the technique
/// TCP-style sequence comparisons use): a forward distance in (0, ModuloHalf] counts as ahead,
/// anything larger counts as behind (a duplicate/stale/late packet).
/// </para>
/// <para>
/// <b>What is evidence and what is assumption:</b> the wrap arithmetic and the "advances by 1"
/// baseline are evidenced by the capture. The classification thresholds are not — the captured
/// traffic has essentially no loss or reordering to calibrate against (that is exactly what
/// "advances by 1 on 99.7%" means), so there is no observed example of a genuine multi-packet
/// gap to confirm this logic against. The 50%-of-window split is a reasonable, conservative
/// choice, not a verified fact about the protocol.
/// </para>
/// <para>
/// Similarly, what the engine actually places in the ack field on a real gap (highest sequence
/// seen so far vs. highest <em>contiguous</em> sequence seen so far — i.e. does it ack ahead of
/// a hole) is not observable in a lossless capture. <see cref="NextAckSequence"/> implements the
/// simpler of the two ("highest seen"), which is what the capture's near-100%-in-order traffic
/// is consistent with, but a real gap could prove that assumption wrong.
/// </para>
/// </remarks>
public sealed class SequenceTracker
{
    /// <summary>Highest send sequence observed from the peer so far. Only meaningful once <see cref="HasSeen"/> is true.</summary>
    public uint HighestSeen { get; private set; }

    /// <summary>True once at least one packet has been observed.</summary>
    public bool HasSeen { get; private set; }

    /// <summary>
    /// The ack value this side should send back: the highest peer send sequence observed. See
    /// the "highest seen vs. highest contiguous" caveat in the type-level remarks.
    /// </summary>
    public uint NextAckSequence => HighestSeen;

    /// <summary>
    /// Forward distance of the most recent <see cref="Gap"/>, in wrapped sequence steps — how
    /// many sequence numbers were skipped. 0 outside of a gap classification.
    /// </summary>
    public uint LastGapSize { get; private set; }

    private const uint Modulo = PacketHeader.SequenceModulo;
    private const uint ModuloHalf = Modulo / 2;

    /// <summary>Records a newly observed peer send sequence and classifies it.</summary>
    public SequenceEvent Observe(uint sendSequence)
    {
        if (!HasSeen)
        {
            HasSeen = true;
            HighestSeen = sendSequence;
            LastGapSize = 0;
            return SequenceEvent.Initial;
        }

        var forwardDistance = (sendSequence - HighestSeen) % Modulo;

        if (forwardDistance == 0)
        {
            LastGapSize = 0;
            return SequenceEvent.Duplicate;
        }

        if (forwardDistance <= ModuloHalf)
        {
            HighestSeen = sendSequence;
            if (forwardDistance == 1)
            {
                LastGapSize = 0;
                return SequenceEvent.InOrder;
            }

            LastGapSize = forwardDistance - 1;
            return SequenceEvent.Gap;
        }

        // forwardDistance > half the wrap window: sendSequence sits "behind" HighestSeen once
        // the wrap is honoured, i.e. this is an old packet arriving late.
        LastGapSize = 0;
        return SequenceEvent.Duplicate;
    }
}

/// <summary>
/// This side's own outgoing 9-bit send-sequence counter (see <see cref="PacketHeader"/>).
/// Separate from <see cref="SequenceTracker"/>, which tracks the peer's counter — the two
/// directions are independent.
/// </summary>
public struct LocalSequenceCounter
{
    /// <summary>The sequence number that will be used by the next call to <see cref="Advance"/>.</summary>
    public uint Current { get; private set; }

    /// <summary>Returns the sequence number to stamp on the next outgoing packet, then advances (wrapping at 512).</summary>
    public uint Advance()
    {
        var value = Current;
        Current = (Current + 1) % PacketHeader.SequenceModulo;
        return value;
    }
}
