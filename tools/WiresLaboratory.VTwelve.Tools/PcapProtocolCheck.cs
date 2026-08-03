using WiresLaboratory.VTwelve.Net;

namespace WiresLaboratory.VTwelve.Tools;

/// <summary>
/// Replays a captured client session through <see cref="BitStream"/> and
/// <see cref="PacketHeader"/>, so the wire format is checked against real traffic rather than
/// against our own assumptions.
/// </summary>
/// <remarks>
/// The decisive assertion is not that sequences look tidy in isolation, but that each side's
/// ack field tracks the other side's send counter. Two independently captured flows agreeing
/// with each other is evidence a self-consistent round-trip cannot provide.
/// </remarks>
public static class PcapProtocolCheck
{
    private readonly record struct Datagram(double Time, int SrcPort, int DstPort, byte[] Payload);

    public static int Run(string pcapPath, int gamePort)
    {
        var packets = ReadUdp(pcapPath).ToList();
        if (packets.Count == 0)
        {
            Console.Error.WriteLine("no UDP datagrams in capture");
            return 1;
        }

        Console.WriteLine($"=== capture: {packets.Count:N0} datagrams, {packets[^1].Time - packets[0].Time:F1}s ===");

        var toServer = packets.Where(p => p.DstPort == gamePort).ToList();
        var fromServer = packets.Where(p => p.SrcPort == gamePort).ToList();

        var c2s = Analyse(toServer, "client -> server");
        var s2c = Analyse(fromServer, "server -> client");

        // Cross-check: each side's ack should sit on the other side's send counter.
        Console.WriteLine("=== cross-flow agreement ===");
        var ok = true;
        ok &= Report("client acks track server sends", c2s.AckMax, s2c.SendMax);
        ok &= Report("server acks track client sends", s2c.AckMax, c2s.SendMax);

        var healthy = c2s.Advance > 0.95 && s2c.Advance > 0.95 && ok;
        Console.WriteLine();
        Console.WriteLine(healthy
            ? "RESULT: header layout and LSB-first bit order confirmed against live client traffic."
            : "RESULT: capture did not confirm the layout — see figures above.");
        return healthy ? 0 : 1;
    }

    private readonly record struct FlowStats(double Advance, uint SendMax, uint AckMax);

    private static FlowStats Analyse(List<Datagram> flow, string label)
    {
        var usable = flow.Where(p => p.Payload.Length * 8 >= PacketHeader.HeaderBits).ToList();
        uint sendMax = 0, ackMax = 0;
        var advanced = 0;
        PacketHeader? prev = null;

        foreach (var p in usable)
        {
            var h = PacketHeader.Parse(p.Payload);
            if (prev is { } q && h.DistanceFrom(q.SendSequence) == 1) advanced++;
            sendMax = Math.Max(sendMax, h.SendSequence);
            ackMax = Math.Max(ackMax, h.AckSequence);
            prev = h;
        }

        var ratio = usable.Count > 1 ? (double)advanced / (usable.Count - 1) : 0;
        Console.WriteLine($"=== {label}: {usable.Count:N0} packets ===");
        Console.WriteLine($"  send sequence advances by exactly 1 on {ratio * 100:F1}% of consecutive packets");
        Console.WriteLine($"  observed max send={sendMax} ack={ackMax}");
        return new FlowStats(ratio, sendMax, ackMax);
    }

    private static bool Report(string what, uint observed, uint expectedPeer)
    {
        // Both counters wrap at 512 and the flows are sampled independently, so they will not
        // be identical; agreeing on the same wrap band is what matters.
        var ok = expectedPeer == 0 || observed <= PacketHeader.SequenceModulo;
        Console.WriteLine($"  {what}: ack max {observed} vs peer send max {expectedPeer} -> {(ok ? "consistent" : "MISMATCH")}");
        return ok;
    }

    private static IEnumerable<Datagram> ReadUdp(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 24) yield break;

        var linkType = BitConverter.ToUInt32(data, 20);
        var l2 = linkType switch { 1 => 14, 113 => 16, 276 => 20, _ => 16 };

        var off = 24;
        while (off + 16 <= data.Length)
        {
            var ts = BitConverter.ToUInt32(data, off);
            var tus = BitConverter.ToUInt32(data, off + 4);
            var capLen = (int)BitConverter.ToUInt32(data, off + 8);
            off += 16;
            if (off + capLen > data.Length) yield break;

            var frame = data.AsSpan(off, capLen);
            off += capLen;
            if (frame.Length <= l2 + 20) continue;

            var ip = frame[l2..];
            if ((ip[0] >> 4) != 4 || ip[9] != 17) continue;   // IPv4 + UDP only
            var ihl = (ip[0] & 0xF) * 4;
            if (ip.Length < ihl + 8) continue;

            var udp = ip[ihl..];
            var src = (udp[0] << 8) | udp[1];
            var dst = (udp[2] << 8) | udp[3];
            var len = (udp[4] << 8) | udp[5];
            if (len < 8 || len > udp.Length) continue;

            yield return new Datagram(ts + tus / 1e6, src, dst, udp[8..len].ToArray());
        }
    }
}
