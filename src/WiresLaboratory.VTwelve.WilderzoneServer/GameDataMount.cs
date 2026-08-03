using WiresLaboratory.VTwelve.Resources;
using WiresLaboratory.VTwelve.Script.Dso;

namespace WiresLaboratory.VTwelve.WilderzoneServer;

/// <summary>
/// The resource view a booting server sees: the <c>base</c> volumes plus, when a ruleset is
/// selected, that ruleset's directory layered on top.
/// </summary>
/// <remarks>
/// This mirrors the original engine's search order — a mod directory shadows <c>base</c>, and
/// loose files on disk shadow the contents of a volume. Nothing is executed here; mounting is
/// separated from execution so the resource pipeline can be verified on its own.
/// </remarks>
public sealed class GameDataMount : IDisposable
{
    private readonly List<Vl2Archive> _volumes = [];

    private GameDataMount(string gameDir, string? ruleset)
    {
        GameDir = gameDir;
        Ruleset = ruleset;
    }

    public string GameDir { get; }
    public string? Ruleset { get; }
    public IReadOnlyList<Vl2Archive> Volumes => _volumes;

    /// <summary>Loose (on-disk) compiled code blocks, mod directory first.</summary>
    public IReadOnlyList<string> CodeBlockPaths { get; private set; } = [];

    public static GameDataMount Open(string gameDir, string? ruleset)
    {
        if (!Directory.Exists(gameDir))
            throw new DirectoryNotFoundException($"GameData directory not found: {gameDir}");

        var mount = new GameDataMount(gameDir, NormalizeRuleset(ruleset));

        // The engine only mounts .vl2 packs sitting directly in base/.
        var baseDir = Path.Combine(gameDir, "base");
        if (Directory.Exists(baseDir))
        {
            foreach (var v in Directory.GetFiles(baseDir, "*.vl2").Order())
            {
                try { mount._volumes.Add(Vl2Archive.Open(v)); }
                catch (Exception ex) { Console.Error.WriteLine($"  ! volume {Path.GetFileName(v)}: {ex.Message}"); }
            }
        }

        var searchDirs = new List<string>();
        if (mount.Ruleset is { } r)
        {
            var modDir = Path.Combine(gameDir, r);
            if (Directory.Exists(modDir)) searchDirs.Add(modDir);
            else Console.Error.WriteLine($"  ! ruleset directory not found: {modDir}");
        }
        if (Directory.Exists(baseDir)) searchDirs.Add(baseDir);

        mount.CodeBlockPaths = searchDirs
            .SelectMany(d => Directory.GetFiles(d, "*.dso", SearchOption.AllDirectories))
            .ToList();

        return mount;
    }

    /// <summary>Parses every loose code block, returning the successes and any failures.</summary>
    public (List<DsoFile> Loaded, List<(string Path, string Error)> Failed) LoadCodeBlocks()
    {
        var loaded = new List<DsoFile>();
        var failed = new List<(string, string)>();
        foreach (var p in CodeBlockPaths)
        {
            try { loaded.Add(DsoFile.Load(p)); }
            catch (Exception ex) { failed.Add((p, ex.Message)); }
        }
        return (loaded, failed);
    }

    // "" / "base" (any case) means stock rules, i.e. no mod layer.
    private static string? NormalizeRuleset(string? r)
    {
        r = r?.Trim();
        return string.IsNullOrEmpty(r) || r.Equals("base", StringComparison.OrdinalIgnoreCase) ? null : r;
    }

    public void Dispose()
    {
        foreach (var v in _volumes) v.Dispose();
        _volumes.Clear();
    }
}
