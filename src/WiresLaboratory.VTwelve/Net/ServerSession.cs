using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// Server-side state for one remote endpoint across the connection lifecycle: the handshake
/// state machine (<see cref="ConnectionState"/>), the session token this side issued and must
/// see echoed back, and the send/ack sequence bookkeeping for once the session reaches
/// <see cref="ConnectionState.Connected"/>.
/// </summary>
/// <remarks>
/// One instance per remote endpoint, owned by <see cref="ServerSessionTable"/>. Not
/// thread-safe — the table is expected to serialize access per endpoint (e.g. by only touching
/// a given session from the thread/task processing that endpoint's datagrams).
/// </remarks>
public sealed class ServerSession
{
    public IPEndPoint RemoteEndPoint { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>Set from ConnectChallengeRequest; must match on every later handshake step.</summary>
    public uint ClientNonce { get; private set; }

    /// <summary>Set from ConnectChallengeRequest; must match on every later handshake step.</summary>
    public uint ClientToken { get; private set; }

    /// <summary>The token this session minted in ConnectChallengeResponse. Valid once <see cref="State"/> is past <see cref="ConnectionState.Disconnected"/>.</summary>
    public uint SessionToken { get; private set; }

    /// <summary>This side's own outgoing 9-bit send sequence counter (see <see cref="PacketHeader"/>). Only meaningful once <see cref="ConnectionState.Connected"/>.</summary>
    public LocalSequenceCounter LocalSequence;

    /// <summary>Tracks the peer's in-session send sequence and produces the ack to send back. Only meaningful once <see cref="ConnectionState.Connected"/>.</summary>
    public SequenceTracker PeerSequence { get; } = new();

    public ServerSession(IPEndPoint remoteEndPoint)
    {
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>
    /// Cryptographically random 32-bit token generator for production use — a spoofed source
    /// address must not be able to predict the token it needs to echo back.
    /// </summary>
    public static uint MintRandomSessionToken()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    /// <summary>
    /// Handles an incoming ConnectChallengeRequest (0x1a): records the client's nonce and
    /// token, mints a session token, and moves <see cref="Disconnected"/> -&gt;
    /// <see cref="ConnectionState.ChallengeSent"/>. Valid from any state — a repeated or
    /// stale-retry challenge request restarts the handshake, matching what a client that never
    /// saw the first response would do.
    /// </summary>
    /// <param name="request">The parsed ConnectChallengeRequest.</param>
    /// <param name="mintSessionToken">
    /// Supplies the session token to issue. Injected rather than always generated internally so
    /// callers can use <see cref="MintRandomSessionToken"/> in production and a fixed value when
    /// replaying a capture for verification (where the token must be observable/comparable
    /// rather than random).
    /// </param>
    /// <returns>
    /// The ConnectChallengeResponse to send. Only the header fields (nonce, session token,
    /// client token) are populated — see <see cref="Net.ConnectChallengeResponse"/>'s doc
    /// comment for why the trailer is left empty rather than guessed at.
    /// </returns>
    public ConnectChallengeResponse OnConnectChallengeRequest(ConnectChallengeRequest request, Func<uint> mintSessionToken)
    {
        ClientNonce = request.ClientNonce;
        ClientToken = request.ClientToken;
        SessionToken = mintSessionToken();
        State = ConnectionState.ChallengeSent;

        return new ConnectChallengeResponse(ClientNonce, SessionToken, ClientToken, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    /// Handles an incoming ConnectRequest (0x20): validates the echoed session token (and, as a
    /// belt-and-braces check, the nonce/client token) against what this session issued and
    /// captured. On success moves <see cref="ConnectionState.ChallengeSent"/> -&gt;
    /// <see cref="ConnectionState.ChallengeReceived"/>.
    /// </summary>
    /// <exception cref="HandshakeException">
    /// The session is not awaiting a ConnectRequest, or the echoed session token does not match
    /// what was issued — the anti-spoofing check described in the task brief.
    /// </exception>
    public void OnConnectRequest(ConnectRequest request)
    {
        if (State != ConnectionState.ChallengeSent)
            throw new HandshakeException(
                $"ConnectRequest received for {RemoteEndPoint} in state {State}, expected {ConnectionState.ChallengeSent}");

        if (request.SessionToken != SessionToken)
            throw new HandshakeException(
                $"session token mismatch for {RemoteEndPoint}: issued 0x{SessionToken:x8}, got 0x{request.SessionToken:x8}");

        if (request.ClientToken != ClientToken || request.ClientNonce != ClientNonce)
            throw new HandshakeException(
                $"client token/nonce mismatch for {RemoteEndPoint} against the original ConnectChallengeRequest");

        State = ConnectionState.ChallengeReceived;
    }

    /// <summary>
    /// Completes the handshake: builds ConnectAccept and moves
    /// <see cref="ConnectionState.ChallengeReceived"/> -&gt; <see cref="ConnectionState.Connected"/>.
    /// Kept as a step separate from <see cref="OnConnectRequest"/> so a caller can run its own
    /// admission checks (slot limits, bans, etc.) between validating the token and actually
    /// accepting the client.
    /// </summary>
    /// <param name="extra">
    /// The fourth ConnectAccept field — observed on the wire but its meaning is not established
    /// (see <see cref="Net.ConnectAccept"/>'s doc comment). Left to the caller rather than
    /// invented here.
    /// </param>
    /// <exception cref="HandshakeException">The session has not passed a validated ConnectRequest yet.</exception>
    public ConnectAccept Accept(uint extra)
    {
        if (State != ConnectionState.ChallengeReceived)
            throw new HandshakeException(
                $"Accept called for {RemoteEndPoint} in state {State}, expected {ConnectionState.ChallengeReceived}");

        State = ConnectionState.Connected;
        return new ConnectAccept(SessionToken, ClientToken, ClientNonce, extra);
    }

    /// <summary>Handles Disconnect (0x26) from either side. Valid from any state.</summary>
    public void OnDisconnect()
    {
        State = ConnectionState.Disconnected;
    }
}

/// <summary>
/// Owns one <see cref="ServerSession"/> per remote endpoint and routes incoming connection
/// control packets to the right one, creating a session on ConnectChallengeRequest and dropping
/// it on Disconnect.
/// </summary>
/// <remarks>
/// This is the "server-side session tracking" called for in the task brief: per-remote-endpoint
/// state, the issued session token, and send/ack sequence counters. It knows nothing about
/// sockets — callers hand it already-parsed handshake packets plus the source
/// <see cref="IPEndPoint"/> and get back the reply to send, or a session to key further work
/// (e.g. in-session sequence tracking) off of.
/// </remarks>
public sealed class ServerSessionTable
{
    private readonly Dictionary<IPEndPoint, ServerSession> _sessions = [];

    public IReadOnlyDictionary<IPEndPoint, ServerSession> Sessions => _sessions;

    public ServerSession? TryGet(IPEndPoint remoteEndPoint) =>
        _sessions.TryGetValue(remoteEndPoint, out var session) ? session : null;

    /// <summary>
    /// Handles ConnectChallengeRequest: creates a fresh session for this endpoint (replacing any
    /// prior one — a new challenge request means the client is (re)starting a handshake, so a
    /// stale in-progress session for the same address/port is superseded rather than left
    /// dangling) and returns the response to send back.
    /// </summary>
    public ConnectChallengeResponse HandleConnectChallengeRequest(
        IPEndPoint remoteEndPoint,
        ConnectChallengeRequest request,
        Func<uint>? mintSessionToken = null)
    {
        var session = new ServerSession(remoteEndPoint);
        _sessions[remoteEndPoint] = session;
        return session.OnConnectChallengeRequest(request, mintSessionToken ?? ServerSession.MintRandomSessionToken);
    }

    /// <summary>Handles ConnectRequest: looks up the session and validates the token echo.</summary>
    /// <exception cref="HandshakeException">No session is in progress for this endpoint, or validation fails.</exception>
    public ServerSession HandleConnectRequest(IPEndPoint remoteEndPoint, ConnectRequest request)
    {
        var session = TryGet(remoteEndPoint)
            ?? throw new HandshakeException($"ConnectRequest from {remoteEndPoint} with no prior ConnectChallengeRequest");
        session.OnConnectRequest(request);
        return session;
    }

    /// <summary>Handles Disconnect: transitions the session (if any) and removes it from the table.</summary>
    public void HandleDisconnect(IPEndPoint remoteEndPoint)
    {
        if (_sessions.Remove(remoteEndPoint, out var session))
            session.OnDisconnect();
    }
}
