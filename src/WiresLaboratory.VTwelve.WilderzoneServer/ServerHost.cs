using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WiresLaboratory.VTwelve.Net;
using WiresLaboratory.VTwelve.Sim.Process;

namespace WiresLaboratory.VTwelve.WilderzoneServer;

/// <summary>
/// The running server: a UDP socket, the connection state machine, and the simulation tick
/// loop driven together.
/// </summary>
/// <remarks>
/// <para>
/// This is the first point at which the recovered pieces actually run as a system rather than
/// as isolated types — the packet header and control-packet classification, the handshake
/// state machine and session table, the 9-bit sequence tracking, and the fixed-timestep
/// process list.
/// </para>
/// <para>
/// <b>A stock client cannot complete a connection against this yet, by design and not by
/// oversight.</b> The engine's <c>ConnectChallengeResponse</c> carries a trailer that is an RSA
/// challenge encrypted under the client's own public key, taken from the certificate the client
/// presents. That mechanism is understood and documented (see
/// <c>WiresLaboratory.NextMastery/HandshakeAuthentication.md</c>) but the big-integer work is
/// not implemented, so the response this host emits has an empty trailer and a real client will
/// reject it. Everything up to and including that point is exercised.
/// </para>
/// <para>
/// The receive loop and the tick are interleaved in one thread deliberately: the simulation is
/// single-threaded in the original, object lifetime during a tick pass is only safe because of
/// the link-shuffle in <see cref="ProcessList"/>, and introducing concurrency here would create
/// a class of bug that does not exist in the thing being reproduced.
/// </para>
/// </remarks>
public sealed class ServerHost : IDisposable
{
    private readonly Socket _socket;
    private readonly ServerSessionTable _sessions = new();
    private readonly ProcessList _simulation;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _receiveBuffer = new byte[2048];

    private long _lastAdvanceMs;
    private long _datagramsIn;
    private long _controlIn;
    private long _dataIn;
    private long _unknownControl;
    private long _ticks;

    public ServerHost(IPEndPoint bind, uint tickMilliseconds = ProcessList.StockTickMilliseconds)
    {
        _simulation = new ProcessList(tickMilliseconds);
        _socket = new Socket(bind.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(bind);
        LocalEndPoint = (IPEndPoint)(_socket.LocalEndPoint ?? bind);
    }

    public IPEndPoint LocalEndPoint { get; }
    public ProcessList Simulation => _simulation;
    public IReadOnlyDictionary<IPEndPoint, ServerSession> Sessions => _sessions.Sessions;

    public long DatagramsReceived => _datagramsIn;
    public long ControlPacketsReceived => _controlIn;
    public long DataPacketsReceived => _dataIn;
    public long UnknownControlPackets => _unknownControl;
    public long TicksRun => _ticks;

    /// <summary>
    /// Runs until cancelled. Services the socket and advances the simulation from the same
    /// thread; see the concurrency note on this type for why.
    /// </summary>
    public void Run(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // A poll timeout shorter than the tick keeps the simulation advancing on schedule
            // even when no traffic is arriving.
            if (_socket.Poll(TimeSpan.FromMilliseconds(4), SelectMode.SelectRead))
                DrainSocket();

            Advance();
        }
    }

    private void DrainSocket()
    {
        while (_socket.Available > 0)
        {
            EndPoint from = new IPEndPoint(
                _socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            int len;
            try
            {
                len = _socket.ReceiveFrom(_receiveBuffer, ref from);
            }
            catch (SocketException)
            {
                // An ICMP port-unreachable for a previous send surfaces here on a connectionless
                // socket. It says nothing about this datagram, so drop it and carry on.
                continue;
            }

            if (len <= 0) continue;
            _datagramsIn++;
            Dispatch((IPEndPoint)from, _receiveBuffer.AsSpan(0, len));
        }
    }

    private void Dispatch(IPEndPoint from, ReadOnlySpan<byte> datagram)
    {
        if (!ControlPacket.IsControl(datagram))
        {
            _dataIn++;
            TrackDataPacket(from, datagram);
            return;
        }

        _controlIn++;
        var type = ControlPacket.TryGetType(datagram);
        if (type is null)
        {
            // Recorded rather than guessed at: an unrecognised control byte is a gap in what has
            // been observed, and inventing a handler for it would bake in an assumption.
            _unknownControl++;
            return;
        }

        switch (type)
        {
            case ControlPacketType.ConnectChallengeRequest:
                OnChallengeRequest(from, datagram);
                break;
            case ControlPacketType.ConnectRequest:
                OnConnectRequest(from, datagram);
                break;
            case ControlPacketType.Disconnect:
                _sessions.HandleDisconnect(from);
                break;
            case ControlPacketType.InfoRequest:
            case ControlPacketType.StatusRequest:
                // The query payload formats are not decoded. Staying silent is the honest
                // behaviour: a fabricated reply would be worse than no reply.
                break;
            default:
                break;
        }
    }

    private void OnChallengeRequest(IPEndPoint from, ReadOnlySpan<byte> datagram)
    {
        ConnectChallengeRequest request;
        try
        {
            request = ConnectChallengeRequest.Parse(datagram);
        }
        catch (Exception)
        {
            return;
        }

        var response = _sessions.HandleConnectChallengeRequest(from, request);
        Send(from, response.Write());
    }

    private void OnConnectRequest(IPEndPoint from, ReadOnlySpan<byte> datagram)
    {
        ConnectRequest request;
        try
        {
            request = ConnectRequest.Parse(datagram);
        }
        catch (Exception)
        {
            return;
        }

        ServerSession session;
        try
        {
            session = _sessions.HandleConnectRequest(from, request);
        }
        catch (HandshakeException)
        {
            // Token mismatch or wrong state: the peer does not hold what this server issued, so
            // it is not admitted. Silence is the correct response to an unauthenticated peer.
            return;
        }

        // The fourth ConnectAccept field's meaning is unestablished, so it is not invented here.
        var accept = session.Accept(extra: 0);
        Send(from, accept.Write());
    }

    private void TrackDataPacket(IPEndPoint from, ReadOnlySpan<byte> datagram)
    {
        var session = _sessions.TryGet(from);
        if (session is null || datagram.Length * 8 < PacketHeader.HeaderBits) return;

        var header = PacketHeader.Parse(datagram);
        session.PeerSequence.Observe(header.SendSequence);
    }

    private void Send(IPEndPoint to, byte[] payload)
    {
        try
        {
            _socket.SendTo(payload, to);
        }
        catch (SocketException)
        {
            // Unreachable peers are expected on a connectionless socket and are not this
            // server's problem; the session ages out through the normal disconnect path.
        }
    }

    private void Advance()
    {
        var now = _clock.ElapsedMilliseconds;
        var elapsed = now - _lastAdvanceMs;
        if (elapsed < _simulation.TickMilliseconds) return;

        _lastAdvanceMs = now;
        _ticks += _simulation.AdvanceServerTime((uint)elapsed);
    }

    public void Dispose() => _socket.Dispose();
}
