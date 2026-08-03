using System.Text.RegularExpressions;

namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// Reads EngineConsoleSurface.md — the usage-string survey recovered from the shipped binary —
/// into the list of commands it documents, for <see cref="ConsoleRegistry.GetCoverage"/> to
/// check registrations against.
/// </summary>
/// <remarks>
/// Parses structurally rather than trusting every line to be a clean signature: every non-blank
/// line inside a fenced code block counts as one manifest entry, which is exactly how the
/// document arrives at its own per-section counts (e.g. "## (global) (299)" has 299 non-blank
/// fenced lines — verified by direct count, not assumed). That matters because at least one
/// line is not a "name(args)" signature at all — <c>SLOVAKIA (Slovak Republic)</c>, a Windows
/// locale string that matched the survey's own recovery heuristic closely enough to slip past
/// its filter (see EngineConsoleSurface.md's own note that "plainly not commands" entries were
/// filtered — this one evidently wasn't). Dropping lines like that would silently undercount the
/// stated 304 total; instead they become an entry with a best-effort name (first whitespace
/// token) that will correctly never match a real registration, so it always shows up as missing
/// rather than vanishing from the denominator.
/// </remarks>
public static class ConsoleManifest
{
    // Global: name immediately followed by "(" — no space, which is what excludes the
    // "SLOVAKIA (Slovak Republic)" line (space before the paren) from matching here.
    private static readonly Regex GlobalSignature = new(@"^([A-Za-z_]\w*)\((.*)\)\s*;?\s*$", RegexOptions.Compiled);

    // Namespaced: "Class::method(args)", as every line in a non-(global) section is written.
    private static readonly Regex NamespacedSignature =
        new(@"^(\w+)::(\w+)\s*\((.*)\)\s*;?\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<ConsoleManifestEntry> Load(string path) => Parse(File.ReadAllLines(path));

    public static IReadOnlyList<ConsoleManifestEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<ConsoleManifestEntry>();
        var inFence = false;
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed == "```")
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence || trimmed.Length == 0) continue;
            entries.Add(ParseLine(trimmed));
        }
        return entries;
    }

    private static ConsoleManifestEntry ParseLine(string line)
    {
        var namespaced = NamespacedSignature.Match(line);
        if (namespaced.Success)
            return new ConsoleManifestEntry(namespaced.Groups[1].Value, namespaced.Groups[2].Value, line);

        var global = GlobalSignature.Match(line);
        if (global.Success)
            return new ConsoleManifestEntry(null, global.Groups[1].Value, line);

        // Doesn't fit either signature shape. Keep it as an entry (see remarks) rather than
        // dropping it, using the line's first token as a best-effort name.
        var firstToken = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? line;
        return new ConsoleManifestEntry(null, firstToken, line);
    }
}
