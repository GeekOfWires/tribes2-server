namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// Phase of a single remote endpoint's connection handshake, as seen by this (server) side of
/// the wire.
/// </summary>
/// <remarks>
/// <para>
/// Named after the four-step handshake recovered from the capture (see
/// <see cref="ControlPacketType"/> and the connect-tokens exchange in
/// <see cref="ConnectTokens"/>): the server receives <c>ConnectChallengeRequest</c> (0x1a) and
/// answers with <c>ConnectChallengeResponse</c> (0x1e), minting a session token
/// (-&gt; <see cref="ChallengeSent"/>); the client must echo that token back in
/// <c>ConnectRequest</c> (0x20), which is validated (-&gt; <see cref="ChallengeReceived"/>); the
/// server answers with <c>ConnectAccept</c> (0x24) (-&gt; <see cref="Connected"/>); either side can
/// end the session with <c>Disconnect</c> (0x26) from any state, returning to
/// <see cref="Disconnected"/>.
/// </para>
/// <para>
/// This ordering — Disconnected -&gt; ChallengeSent -&gt; ChallengeReceived -&gt; Connected -&gt;
/// Disconnected — was observed identically in both of the connect/play/disconnect cycles in
/// <c>tools/fixtures/t2-connect-sessions.pcap</c>.
/// </para>
/// </remarks>
public enum ConnectionState
{
    /// <summary>No handshake in progress for this endpoint (initial state, and the state after Disconnect).</summary>
    Disconnected,

    /// <summary>ConnectChallengeResponse has been sent, issuing a session token; waiting for the client to echo it.</summary>
    ChallengeSent,

    /// <summary>ConnectRequest arrived and its session token matched what was issued; ConnectAccept not sent yet.</summary>
    ChallengeReceived,

    /// <summary>ConnectAccept has been sent; the session is fully established.</summary>
    Connected,
}

/// <summary>
/// Thrown when a handshake packet cannot be accepted in a session's current
/// <see cref="ConnectionState"/>, or when a returned session token does not match the one the
/// session issued.
/// </summary>
/// <remarks>
/// The token check is the anti-spoofing mechanism described in the task brief: a forged source
/// address cannot complete a connection whose ConnectChallengeResponse — and therefore the
/// session token it must echo back — it never actually received.
/// </remarks>
public sealed class HandshakeException(string message) : Exception(message);
