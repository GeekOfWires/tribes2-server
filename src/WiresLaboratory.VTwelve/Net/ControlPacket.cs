using System.Buffers.Binary;

namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// Connection-control packet types, observed on the wire between the stock client and the
/// reference server.
/// </summary>
/// <remarks>
/// Bit 0 of the first byte discriminates control from in-session data: it is 0 for every one
/// of these and 1 for every sequenced data packet. That split was verified across a full
/// capture (28 control packets against 4,540 data packets, with no exceptions).
/// <para>
/// The remaining bits do encode the type, but the exact field boundary is not yet pinned —
/// too few distinct types have been observed to separate a type field from adjacent flags
/// with confidence. These are therefore modelled as the observed discriminant bytes rather
/// than as a decoded bitfield, so the code claims only what the capture actually shows.
/// </para>
/// </remarks>
public enum ControlPacketType : byte
{
    // ---- query protocol -------------------------------------------------------------
    // Observed as two request/response pairs, repeating in bursts during and between
    // sessions: a 6-byte request draws a 48-byte reply, and a second 6-byte request draws
    // a ~183-byte reply. The pairing, sizes and timing are what identify these as the
    // browser-facing query surface; their contents are not decoded yet, so the names below
    // describe the observed roles rather than any verified payload.
    InfoRequest = 0x0e,
    InfoResponse = 0x10,
    StatusRequest = 0x12,
    StatusResponse = 0x14,

    // ---- connection establishment ---------------------------------------------------
    /// <summary>Client opens the exchange with a nonce and its token.</summary>
    ConnectChallengeRequest = 0x1a,

    /// <summary>Server echoes the client's values and issues a session token.</summary>
    ConnectChallengeResponse = 0x1e,

    /// <summary>Client returns the server's token, proving it received the response.</summary>
    ConnectRequest = 0x20,

    /// <summary>Server admits the client to the session.</summary>
    ConnectAccept = 0x24,

    /// <summary>Sent by either side to tear the session down.</summary>
    Disconnect = 0x26,
}

/// <summary>
/// The token exchange carried by the connection handshake.
/// </summary>
/// <remarks>
/// The four steps carry the same three 32-bit values in different orders: a client nonce, a
/// client token, and a session token the server mints in its challenge response. The client
/// must echo that server token back in its connect request, which is what stops a spoofed
/// source address from completing a connection it never received the response for.
/// </remarks>
public readonly record struct ConnectTokens(uint ClientNonce, uint ClientToken, uint SessionToken)
{
    /// <summary>Reads the little-endian 32-bit words following the type byte.</summary>
    public static uint[] ReadWords(ReadOnlySpan<byte> payload, int count)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var at = 1 + i * 4;
            if (at + 4 > payload.Length)
                throw new ArgumentException($"payload too short for word {i}", nameof(payload));
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload[at..]);
        }
        return words;
    }
}

/// <summary>Helpers for classifying a datagram before any bit-level decoding.</summary>
public static class ControlPacket
{
    /// <summary>True when the datagram is connection control rather than sequenced data.</summary>
    public static bool IsControl(ReadOnlySpan<byte> datagram) =>
        datagram.Length > 0 && (datagram[0] & 1) == 0;

    /// <summary>
    /// Classifies a control datagram. Returns null for a data packet, or for a control byte
    /// this implementation has not seen on the wire yet — an unknown value is reported rather
    /// than guessed at.
    /// </summary>
    public static ControlPacketType? TryGetType(ReadOnlySpan<byte> datagram)
    {
        if (!IsControl(datagram)) return null;
        var b = datagram[0];
        return Enum.IsDefined(typeof(ControlPacketType), b) ? (ControlPacketType)b : null;
    }
}
