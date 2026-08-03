using WiresLaboratory.VTwelve.Script;
using WiresLaboratory.VTwelve.Sim.Process;
using System.Net;
using WiresLaboratory.NextMastery;

namespace WiresLaboratory.VTwelve.WilderzoneServer;

/// <summary>
/// Standalone entry point for the Wilderzone V12 server.
/// </summary>
/// <remarks>
/// The boot sequence follows the original engine's order, and each stage is added only once
/// it can be verified against the shipped content. Implemented today: resource mounting and
/// compiled-code loading. Still to come: the bytecode VM and console object system, then
/// physics, then ghosting.
/// </remarks>
public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Usage();
            return args.Length == 0 ? 2 : 0;
        }

        var gameDir = args[0];
        var ruleset = ValueOf(args, "--mod");

        Console.WriteLine("Wilderzone — V12 dedicated server (managed)");
        Console.WriteLine($"  GameData : {gameDir}");
        Console.WriteLine($"  Ruleset  : {ruleset ?? "base (stock)"}");
        Console.WriteLine();

        try
        {
            using var mount = GameDataMount.Open(gameDir, ruleset);

            Console.WriteLine("[mount] volumes");
            var entries = mount.Volumes.Sum(v => v.Entries.Count);
            Console.WriteLine($"  {mount.Volumes.Count} volume(s), {entries:N0} entries");

            Console.WriteLine("[script] compiled code blocks");
            var (loaded, failed) = mount.LoadCodeBlocks();
            var slots = loaded.Sum(d => (long)d.CodeSize);
            Console.WriteLine($"  parsed {loaded.Count}/{mount.CodeBlockPaths.Count}, {slots:N0} instruction slots");
            foreach (var (path, error) in failed.Take(5))
                Console.WriteLine($"  ! {Path.GetFileName(path)}: {error}");

            var versions = loaded.Select(d => d.Version).Distinct().Order().ToArray();
            if (versions.Length > 0)
                Console.WriteLine($"  DSO version(s): {string.Join(", ", versions)}");

            var tn = new TribesNextOptions();
            Console.WriteLine($"[next] master {tn.Master} every {tn.HeartbeatSeconds}s (registration not implemented yet)");

            Console.WriteLine();
            Console.WriteLine(failed.Count == 0
                ? "Boot stage complete: resources mounted and all code blocks decoded."
                : $"Boot stage finished with {failed.Count} undecodable code block(s).");

            // Configuration comes from the ruleset's prefs, the same file the engine execs at
            // start-up — not from flags invented here. An operator who has already set
            // $Host::Port for their server gets that port, with no second place to configure it.
            var prefs = ServerPreferences.Load(gameDir, ruleset);
            Console.WriteLine("[prefs] " + (prefs.Path is null
                ? $"none found; using defaults (port {ServerPreferences.DefaultPort})"
                : $"{Path.GetFileName(prefs.Path)}: {prefs.Values.Count} setting(s)"
                  + (prefs.UnparsedLines > 0 ? $", {prefs.UnparsedLines} non-assignment line(s) ignored" : "")));
            Console.WriteLine($"[prefs] $Host::Port = {prefs.Port}"
                              + (prefs.GameName is { } gn ? $"   $Host::GameName = \"{gn}\"" : ""));

            if (args.Contains("--boot-only"))
            {
                Console.WriteLine("--boot-only: stopping before the socket is bound.");
                return failed.Count == 0 ? 0 : 1;
            }

            return Serve(prefs.Port, ValueOf(args, "--tick-ms"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"boot failed: {ex.Message}");
            return 1;
        }
    }

    private static int Serve(int port, string? tickText)
    {
        var tick = ProcessList.StockTickMilliseconds;
        if (tickText is not null)
        {
            if (!uint.TryParse(tickText, out tick))
            {
                Console.Error.WriteLine($"--tick-ms: not a number: {tickText}");
                return 2;
            }
            Console.WriteLine($"[warn] running at {tick}ms rather than the stock "
                              + $"{ProcessList.StockTickMilliseconds}ms — a stock client predicts movement at "
                              + "the stock rate and will disagree with this server.");
        }

        using var host = new ServerHost(new IPEndPoint(IPAddress.Any, port), tick);
        Console.WriteLine($"[net] listening on {host.LocalEndPoint} (udp), tick {host.Simulation.TickMilliseconds}ms");
        Console.WriteLine("[net] a stock client will NOT complete a connection yet: the challenge");
        Console.WriteLine("      response trailer (an RSA challenge under the client's own key) is");
        Console.WriteLine("      documented but not implemented. See NextMastery/HandshakeAuthentication.md.");
        Console.WriteLine("Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var reporter = new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                Thread.Sleep(5000);
                Console.WriteLine($"[stat] ticks={host.TicksRun} datagrams={host.DatagramsReceived} "
                                  + $"(control={host.ControlPacketsReceived} data={host.DataPacketsReceived} "
                                  + $"unknown={host.UnknownControlPackets}) sessions={host.Sessions.Count}");
            }
        }) { IsBackground = true };
        reporter.Start();

        try
        {
            host.Run(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine($"[net] stopped. ticks={host.TicksRun} datagrams={host.DatagramsReceived} "
                          + $"sessions={host.Sessions.Count}");
        return 0;
    }

    private static string? ValueOf(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Usage()
    {
        Console.WriteLine("usage: WiresLaboratory.VTwelve.WilderzoneServer <GameData-dir> [--mod <ruleset>]");
        Console.WriteLine();
        Console.WriteLine("  <GameData-dir>   directory holding base/ and any ruleset directories");
        Console.WriteLine("  --mod <ruleset>  ruleset to layer over base (omit, or 'base', for stock)");
        Console.WriteLine("  --boot-only      verify boot, then stop before binding the socket");
        Console.WriteLine("  --tick-ms <n>    timestep override; stock is 32, and anything else");
        Console.WriteLine("                   breaks movement prediction against a stock client");
    }
}
