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
            Console.WriteLine("Simulation is not implemented yet — the VM, physics and ghosting stages are still to come.");
            return failed.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"boot failed: {ex.Message}");
            return 1;
        }
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
    }
}
