using System.Buffers.Binary;

namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// Step 1 of the handshake: client -&gt; server. Opens the exchange with a nonce and the
/// client's token. 693 bytes in both connect attempts captured in
/// <c>tools/fixtures/t2-connect-sessions.pcap</c>.
/// </summary>
/// <remarks>
/// Layout, byte-offset-verified against both captured instances:
/// <code>
///   byte    0      type    = 0x1a (ControlPacketType.ConnectChallengeRequest)
///   bytes   1..4   client nonce   (u32 LE) - 0x00000034 in both captured sessions
///   bytes   5..8   client token   (u32 LE) - differs per session (0x000d0fef / 0x000de17a)
///   bytes   9..    trailer        (684 bytes)
/// </code>
/// The trailer (which includes what may or may not be a distinct flag byte at offset 9 —
/// observed as 0x00 both times, but two samples cannot separate "always zero flag" from "start
/// of a blob that happens to begin with a zero byte") is not decoded. Per the task brief this is
/// presumed to be TribesNext auth/CD-key material; the bytes look uniformly random and are
/// <em>not</em> reproduced between the two sessions, so there is nothing here to reverse further
/// without more captures. It is carried through opaquely so a captured packet round-trips
/// byte-for-byte via <see cref="Parse"/> + <see cref="Write"/>.
/// </remarks>
public readonly record struct ConnectChallengeRequest(uint ClientNonce, uint ClientToken, ReadOnlyMemory<byte> Trailer)
{
    public const byte Type = (byte)ControlPacketType.ConnectChallengeRequest;

    /// <summary>type + nonce + token; everything after this is the opaque trailer.</summary>
    private const int HeaderLength = 9;

    public static ConnectChallengeRequest Parse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderLength)
            throw new ArgumentException(
                $"{nameof(ConnectChallengeRequest)} needs at least {HeaderLength} bytes, got {datagram.Length}",
                nameof(datagram));
        if (datagram[0] != Type)
            throw new ArgumentException($"expected type 0x{Type:x2}, got 0x{datagram[0]:x2}", nameof(datagram));

        var words = ConnectTokens.ReadWords(datagram, 2);
        return new ConnectChallengeRequest(words[0], words[1], datagram[HeaderLength..].ToArray());
    }

    public byte[] Write()
    {
        var buffer = new byte[HeaderLength + Trailer.Length];
        buffer[0] = Type;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), ClientNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(5), ClientToken);
        Trailer.Span.CopyTo(buffer.AsSpan(HeaderLength));
        return buffer;
    }
}

/// <summary>
/// Step 2 of the handshake: server -&gt; client. Echoes the client's nonce and token, and mints
/// the session token the client must return. 143 bytes in both captured instances.
/// </summary>
/// <remarks>
/// Layout, byte-offset-verified against both captured instances:
/// <code>
///   byte    0       type    = 0x1e (ControlPacketType.ConnectChallengeResponse)
///   bytes   1..4    client nonce    (u32 LE) - echoes the request
///   bytes   5..8    session token   (u32 LE) - newly minted by the server (0x0003dc68 / 0x0004e632)
///   bytes   9..12   client token    (u32 LE) - echoes the request
///   bytes   13..    trailer         (130 bytes)
/// </code>
/// Field order here (nonce, session token, client token) differs from the order used by
/// <see cref="ConnectRequest"/> and <see cref="ConnectAccept"/> (session token, client token,
/// nonce) — this is simply what the capture shows, not a normalised choice.
/// <para>
/// The trailer (starting with a byte observed as 0x01 both times, then 129 further bytes that
/// do differ session-to-session) is not decoded. This is the more consequential unknown: it is
/// presumably the server's half of a TribesNext auth/CD-key exchange, and without knowing its
/// contents a from-scratch server cannot construct a reply the stock client will actually
/// accept — this type can parse a captured response, but <see cref="Write"/> only reproduces
/// whatever trailer bytes it is given (empty by default), which is very unlikely to satisfy a
/// real client. That gap is left unimplemented rather than guessed at.
/// </para>
/// </remarks>
public readonly record struct ConnectChallengeResponse(
    uint ClientNonce,
    uint SessionToken,
    uint ClientToken,
    ReadOnlyMemory<byte> Trailer)
{
    public const byte Type = (byte)ControlPacketType.ConnectChallengeResponse;

    /// <summary>type + nonce + session token + client token; everything after is the opaque trailer.</summary>
    private const int HeaderLength = 13;

    public static ConnectChallengeResponse Parse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderLength)
            throw new ArgumentException(
                $"{nameof(ConnectChallengeResponse)} needs at least {HeaderLength} bytes, got {datagram.Length}",
                nameof(datagram));
        if (datagram[0] != Type)
            throw new ArgumentException($"expected type 0x{Type:x2}, got 0x{datagram[0]:x2}", nameof(datagram));

        var words = ConnectTokens.ReadWords(datagram, 3);
        return new ConnectChallengeResponse(words[0], words[1], words[2], datagram[HeaderLength..].ToArray());
    }

    public byte[] Write()
    {
        var buffer = new byte[HeaderLength + Trailer.Length];
        buffer[0] = Type;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), ClientNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(5), SessionToken);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(9), ClientToken);
        Trailer.Span.CopyTo(buffer.AsSpan(HeaderLength));
        return buffer;
    }
}

/// <summary>
/// Step 3 of the handshake: client -&gt; server. Returns the server-minted session token,
/// proving the client actually received <see cref="ConnectChallengeResponse"/>. 85 bytes in
/// both captured instances.
/// </summary>
/// <remarks>
/// Layout, byte-offset-verified against both captured instances:
/// <code>
///   byte    0       type    = 0x20 (ControlPacketType.ConnectRequest)
///   bytes   1..4    session token   (u32 LE) - must equal the one issued in step 2
///   bytes   5..8    client token    (u32 LE) - echoes the original request
///   bytes   9..12   client nonce    (u32 LE) - echoes the original request
///   bytes   13..    trailer         (72 bytes)
/// </code>
/// The trailer is not decoded. One observation worth recording without over-claiming it: the
/// final 28 bytes of the trailer were byte-for-byte identical between the two captured sessions,
/// while the preceding ~40 bytes and a 4-byte value immediately before that fixed tail differed.
/// That is consistent with a fixed client/build identifier following session-specific data, but
/// two samples is not enough to call that proven, so it is not modelled — the whole trailer is
/// carried opaquely.
/// </remarks>
public readonly record struct ConnectRequest(
    uint SessionToken,
    uint ClientToken,
    uint ClientNonce,
    ReadOnlyMemory<byte> Trailer)
{
    public const byte Type = (byte)ControlPacketType.ConnectRequest;

    /// <summary>type + session token + client token + nonce; everything after is the opaque trailer.</summary>
    private const int HeaderLength = 13;

    public static ConnectRequest Parse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderLength)
            throw new ArgumentException(
                $"{nameof(ConnectRequest)} needs at least {HeaderLength} bytes, got {datagram.Length}",
                nameof(datagram));
        if (datagram[0] != Type)
            throw new ArgumentException($"expected type 0x{Type:x2}, got 0x{datagram[0]:x2}", nameof(datagram));

        var words = ConnectTokens.ReadWords(datagram, 3);
        return new ConnectRequest(words[0], words[1], words[2], datagram[HeaderLength..].ToArray());
    }

    public byte[] Write()
    {
        var buffer = new byte[HeaderLength + Trailer.Length];
        buffer[0] = Type;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), SessionToken);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(5), ClientToken);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(9), ClientNonce);
        Trailer.Span.CopyTo(buffer.AsSpan(HeaderLength));
        return buffer;
    }
}

/// <summary>
/// Step 4 of the handshake: server -&gt; client. Admits the client to the session. 17 bytes in
/// both captured instances — the only one of the four handshake packets with no undecoded
/// trailer.
/// </summary>
/// <remarks>
/// Layout, byte-offset-verified against both captured instances, and fully accounted for:
/// <code>
///   byte    0       type    = 0x24 (ControlPacketType.ConnectAccept)
///   bytes   1..4    session token   (u32 LE) - echoes what this session issued
///   bytes   5..8    client token    (u32 LE) - echoes the original request
///   bytes   9..12   client nonce    (u32 LE) - echoes the original request
///   bytes   13..16  extra value     (u32 LE) - present, differs per session (0x000009b6 / 0x000009bc)
/// </code>
/// <see cref="Extra"/>'s meaning is not established: two data points, close in value, is not
/// enough evidence to guess whether it is a tick count, a connection id, or something else — it
/// is a fully decoded field (position and width are certain) with an undetermined <em>meaning</em>,
/// which is a different kind of gap than the other packets' opaque trailers.
/// </remarks>
public readonly record struct ConnectAccept(uint SessionToken, uint ClientToken, uint ClientNonce, uint Extra)
{
    public const byte Type = (byte)ControlPacketType.ConnectAccept;
    private const int Length = 17;

    public static ConnectAccept Parse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < Length)
            throw new ArgumentException(
                $"{nameof(ConnectAccept)} needs at least {Length} bytes, got {datagram.Length}", nameof(datagram));
        if (datagram[0] != Type)
            throw new ArgumentException($"expected type 0x{Type:x2}, got 0x{datagram[0]:x2}", nameof(datagram));

        var words = ConnectTokens.ReadWords(datagram, 4);
        return new ConnectAccept(words[0], words[1], words[2], words[3]);
    }

    public byte[] Write()
    {
        var buffer = new byte[Length];
        buffer[0] = Type;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), SessionToken);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(5), ClientToken);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(9), ClientNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(13), Extra);
        return buffer;
    }
}

/// <summary>
/// Tears a session down. Sent by either side; observed identically in both directions and both
/// captured sessions, always 11 bytes.
/// </summary>
/// <remarks>
/// Layout, byte-offset-verified against all four captured instances (two per session, one each
/// direction):
/// <code>
///   byte    0      type    = 0x26 (ControlPacketType.Disconnect)
///   bytes   1..4   session token   (u32 LE)
///   bytes   5..8   client token    (u32 LE)
///   bytes   9..10  trailer         (2 bytes) - 0x0000 in all four captured instances
/// </code>
/// The 2-byte trailer's width and meaning are not established from the capture — all four
/// samples are zero, which is equally consistent with "always-zero reserved bytes", "a reason
/// code that was never exercised", or "the true field boundary is elsewhere and this is
/// coincidentally zero". It is carried as raw bytes rather than interpreted as an integer.
/// </remarks>
public readonly record struct Disconnect(uint SessionToken, uint ClientToken, ReadOnlyMemory<byte> Trailer)
{
    public const byte Type = (byte)ControlPacketType.Disconnect;

    /// <summary>type + session token + client token; everything after is the (unexplained, always-zero-so-far) trailer.</summary>
    private const int HeaderLength = 9;

    public static Disconnect Parse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderLength)
            throw new ArgumentException(
                $"{nameof(Disconnect)} needs at least {HeaderLength} bytes, got {datagram.Length}", nameof(datagram));
        if (datagram[0] != Type)
            throw new ArgumentException($"expected type 0x{Type:x2}, got 0x{datagram[0]:x2}", nameof(datagram));

        var words = ConnectTokens.ReadWords(datagram, 2);
        return new Disconnect(words[0], words[1], datagram[HeaderLength..].ToArray());
    }

    public byte[] Write()
    {
        var buffer = new byte[HeaderLength + Trailer.Length];
        buffer[0] = Type;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), SessionToken);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(5), ClientToken);
        Trailer.Span.CopyTo(buffer.AsSpan(HeaderLength));
        return buffer;
    }

    /// <summary>Convenience for constructing a well-formed Disconnect with the observed all-zero trailer.</summary>
    public static Disconnect Create(uint sessionToken, uint clientToken) =>
        new(sessionToken, clientToken, new byte[2]);
}
