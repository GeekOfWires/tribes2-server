namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// Console command table and dispatcher: the C# home for the surface recovered in
/// EngineFunctionAddresses.md and EngineConsoleSurface.md. Holds both registration shapes the
/// binary's registrar (<c>0x00426450</c>, 388 call sites) produces — bare global functions and
/// <c>Class::method</c> pairs invoked in script as <c>obj.method(args)</c> — and dispatches
/// through the arity check the engine itself performs before ever calling into a handler.
/// </summary>
public sealed class ConsoleRegistry
{
    private readonly Dictionary<string, ConsoleFunctionEntry> _globals = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<(string Namespace, string Method), ConsoleFunctionEntry> _methods =
        new(NamespaceMethodComparer.Instance);

    public IReadOnlyCollection<ConsoleFunctionEntry> GlobalFunctions => _globals.Values;

    public IReadOnlyCollection<ConsoleFunctionEntry> Methods => _methods.Values;

    /// <summary>
    /// Registers a bare global function, e.g. <c>VectorAdd(vec1,vec2)</c>.
    /// </summary>
    /// <remarks>
    /// Lookup is case-insensitive. That is the documented behaviour across the Torque lineage
    /// (script identifiers are resolved case-insensitively) rather than something the recovered
    /// manifest proves for this T2 binary specifically — usage strings show argument shape, not
    /// the comparison the registrar's lookup uses. Treat it as an informed default.
    /// </remarks>
    public void RegisterGlobalFunction(string name, int minArgs, int maxArgs, string usage, ConsoleFunctionDelegate body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _globals[name] = new ConsoleFunctionEntry(name, null, minArgs, maxArgs, usage, body);
    }

    /// <summary>Registers a namespaced method, e.g. <c>AIConnection::setSkillLevel(float)</c>.</summary>
    public void RegisterMethod(
        string namespaceName, string methodName, int minArgs, int maxArgs, string usage, ConsoleFunctionDelegate body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        _methods[(namespaceName, methodName)] = new ConsoleFunctionEntry(methodName, namespaceName, minArgs, maxArgs, usage, body);
    }

    public bool TryGetGlobalFunction(string name, out ConsoleFunctionEntry? entry) => _globals.TryGetValue(name, out entry);

    public bool TryGetMethod(string namespaceName, string methodName, out ConsoleFunctionEntry? entry) =>
        _methods.TryGetValue((namespaceName, methodName), out entry);

    /// <summary>Invokes a global function by name, enforcing its declared arity.</summary>
    public ConsoleInvocationResult InvokeGlobal(string name, IReadOnlyList<ConsoleValue> args)
    {
        if (!TryGetGlobalFunction(name, out var entry) || entry is null)
            return ConsoleInvocationResult.Fail($"{name}: unknown command.");
        return Dispatch(entry, null, args);
    }

    /// <summary>Invokes <c>namespaceName::methodName</c> against <paramref name="target"/>, enforcing its declared arity.</summary>
    public ConsoleInvocationResult InvokeMethod(
        string namespaceName, string methodName, object? target, IReadOnlyList<ConsoleValue> args)
    {
        if (!TryGetMethod(namespaceName, methodName, out var entry) || entry is null)
            return ConsoleInvocationResult.Fail($"{namespaceName}::{methodName}: unknown method.");
        return Dispatch(entry, target, args);
    }

    private static ConsoleInvocationResult Dispatch(ConsoleFunctionEntry entry, object? target, IReadOnlyList<ConsoleValue> args)
    {
        if (!entry.AcceptsArgCount(args.Count))
            // Shape matches what the engine itself prints on a bad call: a one-line complaint
            // plus the usage string recovered in EngineConsoleSurface.md. The exact wording of
            // the engine's own message was not recovered (the manifest captured usage strings,
            // not the surrounding error-template text), so this phrasing is a reasonable
            // reconstruction, not a verified transcript.
            return ConsoleInvocationResult.Fail($"{entry.QualifiedName}: wrong number of arguments.\nusage: {entry.Usage}");

        try
        {
            return ConsoleInvocationResult.Ok(entry.Invoke(target, args));
        }
        catch (Exception ex)
        {
            // A handler throwing is our bug, not a script-level misuse — still reported through
            // the same non-throwing channel so one bad command can't take down whoever is
            // running the script, matching how the real engine isolates command failures.
            return ConsoleInvocationResult.Fail($"{entry.QualifiedName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Cross-references every entry in <paramref name="manifest"/> (see <see cref="ConsoleManifest"/>)
    /// against current registrations, so "how much of the 304 is implemented" is answerable
    /// without a hand-maintained list.
    /// </summary>
    public ConsoleCoverageReport GetCoverage(IReadOnlyList<ConsoleManifestEntry> manifest)
    {
        var missing = new List<ConsoleManifestEntry>();
        var implemented = 0;
        foreach (var entry in manifest)
        {
            var isImplemented = entry.Namespace is null
                ? _globals.ContainsKey(entry.Name)
                : _methods.ContainsKey((entry.Namespace, entry.Name));
            if (isImplemented) implemented++;
            else missing.Add(entry);
        }
        return new ConsoleCoverageReport(manifest.Count, implemented, missing);
    }

    private sealed class NamespaceMethodComparer : IEqualityComparer<(string Namespace, string Method)>
    {
        public static readonly NamespaceMethodComparer Instance = new();

        public bool Equals((string Namespace, string Method) x, (string Namespace, string Method) y) =>
            string.Equals(x.Namespace, y.Namespace, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Method, y.Method, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Namespace, string Method) obj) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Namespace),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Method));
    }
}
