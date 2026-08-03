using WiresLaboratory.VTwelve.Script.Dso;

namespace WiresLaboratory.VTwelve.Tools;

/// <summary>
/// Mines the shipped code blocks for the symbol surface the engine has to provide.
/// </summary>
/// <remarks>
/// A DSO's identifier patch table lists exactly the symbolic names a code block resolves at
/// load time — function names, namespaces and object/field identifiers — together with the
/// number of code slots each one is patched into. Aggregating that across every shipped
/// script turns "implement the console command set" into a ranked worklist backed by the
/// content that actually has to run, rather than a guess at what might be needed.
/// </remarks>
public static class IdentifierSurvey
{
    public sealed record Symbol(string Name, int Files, int References);

    /// <summary>
    /// True for symbols the engine itself must provide. Script-local (<c>%x</c>) and global
    /// (<c>$x</c>) variables are storage the VM allocates, not surface the engine implements,
    /// so they are excluded — otherwise loop counters dominate the ranking.
    /// </summary>
    public static bool IsEngineSurface(string name) =>
        name.Length > 0 && name[0] != '%' && name[0] != '$';

    public static IReadOnlyList<Symbol> Run(IEnumerable<string> dsoPaths, bool engineOnly = false)
    {
        var refs = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var path in dsoPaths)
        {
            DsoFile dso;
            try { dso = DsoFile.Load(path); }
            catch { continue; }

            foreach (var patch in dso.Identifiers)
            {
                var name = dso.GlobalStrings.GetString(patch.StringOffset);
                if (name.Length == 0) continue;
                if (engineOnly && !IsEngineSurface(name)) continue;
                refs[name] = refs.GetValueOrDefault(name) + patch.Slots.Length;
                if (!files.TryGetValue(name, out var set)) files[name] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(path);
            }
        }

        return refs
            .Select(kv => new Symbol(kv.Key, files[kv.Key].Count, kv.Value))
            .OrderByDescending(s => s.References)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static void Report(IReadOnlyList<Symbol> symbols, int top)
    {
        Console.WriteLine($"=== symbol surface: {symbols.Count:N0} distinct identifiers ===");
        Console.WriteLine($"{"references",10}  {"files",5}  name");
        foreach (var s in symbols.Take(top))
            Console.WriteLine($"{s.References,10:N0}  {s.Files,5}  {s.Name}");
    }
}
