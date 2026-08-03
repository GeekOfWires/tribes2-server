using System.Globalization;
using System.Text.RegularExpressions;

namespace WiresLaboratory.VTwelve.Script;

/// <summary>
/// The <c>$Host::*</c> settings a ruleset's <c>prefs/serverprefs.cs</c> declares.
/// </summary>
/// <remarks>
/// <para>
/// This is the engine's own configuration mechanism, not a new one: the engine execs the
/// selected ruleset's <c>prefs/serverPrefs.cs</c> during start-up, and the globals it assigns
/// there are what the server runs on — the listen port, the server name, player limits and so
/// on. A managed host must read the same file so an operator's existing configuration works
/// unchanged, rather than being configured twice through two different mechanisms.
/// </para>
/// <para>
/// Only assignment statements are interpreted. This is deliberately <b>not</b> a TorqueScript
/// interpreter: the file is nominally executable script and may contain arbitrary code, but the
/// shipped and operator-edited forms are flat assignments. Anything more complex is ignored
/// rather than half-executed, and <see cref="UnparsedLines"/> reports how much was skipped so a
/// file that does need real evaluation is visible instead of silently misread.
/// </para>
/// <para>
/// Lookup is case-insensitive in two places, both learned from the shipped content. The file may
/// be named <c>serverPrefs.cs</c> or <c>serverprefs.cs</c> — the engine ran on a case-insensitive
/// filesystem, so mods differ, and a case-sensitive host that picks the wrong one silently loses
/// the operator's settings. Variable names are matched the same way for the same reason.
/// </para>
/// </remarks>
public sealed class ServerPreferences
{
    /// <summary>The port the engine listens on when the prefs do not say otherwise.</summary>
    public const int DefaultPort = 28000;

    private static readonly Regex Assignment = new(
        @"^\s*\$(?<name>[A-Za-z_][A-Za-z0-9_:]*)\s*=\s*(?<value>.+?)\s*;",
        RegexOptions.Compiled);

    private readonly Dictionary<string, string> _values;

    private ServerPreferences(string? path, Dictionary<string, string> values, int unparsed)
    {
        Path = path;
        _values = values;
        UnparsedLines = unparsed;
    }

    /// <summary>The file these values came from, or null when none was found.</summary>
    public string? Path { get; }

    /// <summary>Assigned globals, keyed without the leading <c>$</c>.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>
    /// Non-empty, non-comment lines that were not a simple assignment. A non-zero count means the
    /// file contains script this reader deliberately did not evaluate.
    /// </summary>
    public int UnparsedLines { get; }

    /// <summary>The listen port — <c>$Host::Port</c>, falling back to <see cref="DefaultPort"/>.</summary>
    public int Port => GetInt("Host::Port", DefaultPort);

    /// <summary>The advertised server name — <c>$Host::GameName</c>.</summary>
    public string? GameName => GetString("Host::GameName");

    /// <summary>Maximum players — <c>$Host::MaxPlayers</c>.</summary>
    public int MaxPlayers => GetInt("Host::MaxPlayers", 0);

    /// <summary>
    /// Loads the prefs for a ruleset. <paramref name="ruleset"/> may be null, empty or "base" for
    /// stock rules. Returns an empty instance when no file exists — a missing prefs file is a
    /// normal first-run state, not an error.
    /// </summary>
    public static ServerPreferences Load(string gameDir, string? ruleset)
    {
        var dir = System.IO.Path.Combine(
            gameDir,
            string.IsNullOrWhiteSpace(ruleset) || ruleset.Equals("base", StringComparison.OrdinalIgnoreCase)
                ? "base"
                : ruleset,
            "prefs");

        var file = FindPrefsFile(dir);
        if (file is null)
            return new ServerPreferences(null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), 0);

        return Parse(File.ReadAllLines(file), file);
    }

    /// <summary>Parses prefs content directly. Exposed for testing without a GameData tree.</summary>
    public static ServerPreferences Parse(IEnumerable<string> lines, string? path = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unparsed = 0;

        foreach (var raw in lines)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            var m = Assignment.Match(line);
            if (!m.Success)
            {
                unparsed++;
                continue;
            }

            values[m.Groups["name"].Value] = Unquote(m.Groups["value"].Value.Trim());
        }

        return new ServerPreferences(path, values, unparsed);
    }

    public string? GetString(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public int GetInt(string name, int fallback) =>
        GetString(name) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    public bool GetBool(string name, bool fallback) =>
        GetInt(name, fallback ? 1 : 0) != 0;

    // The engine's config files are written with "//" line comments. A "//" inside a quoted
    // string is not a comment, so quoting is tracked rather than blindly splitting on it.
    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"') inString = !inString;
            else if (!inString && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static string? FindPrefsFile(string prefsDir)
    {
        if (!Directory.Exists(prefsDir)) return null;

        // Case-insensitive match on the whole name: the engine's filesystem did not distinguish
        // serverPrefs.cs from serverprefs.cs, and mods ship both spellings.
        return Directory.EnumerateFiles(prefsDir)
            .FirstOrDefault(f => System.IO.Path.GetFileName(f)
                .Equals("serverprefs.cs", StringComparison.OrdinalIgnoreCase));
    }
}
