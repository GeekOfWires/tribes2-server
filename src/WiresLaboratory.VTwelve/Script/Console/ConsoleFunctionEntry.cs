namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// Body of a registered console command. <paramref name="target"/> is the invoking object for a
/// namespaced method (script <c>obj.method(args)</c>) and always <see langword="null"/> for a
/// global function — mirroring the two registration shapes the binary's registrar produces (see
/// EngineFunctionAddresses.md: 353 corroborated entries split between bare functions like
/// <c>setScale</c> and <c>Class::method</c> pairs like <c>AIConnection::setSkillLevel</c>).
/// Typed as <see cref="object"/> rather than a concrete sim-object type because that hierarchy
/// does not exist in this codebase yet; a caller with a real object model downcasts once it does.
/// </summary>
public delegate ConsoleValue ConsoleFunctionDelegate(object? target, IReadOnlyList<ConsoleValue> args);

/// <summary>
/// One registered console command — either a global function or a <c>Class::method</c>. Carries
/// exactly what the recovered manifest carries per entry: name, optional namespace, an arg-count
/// range, and the usage text the engine would show back on misuse.
/// </summary>
public sealed record ConsoleFunctionEntry(
    string Name,
    string? Namespace,
    int MinArgs,
    int MaxArgs,
    string Usage,
    ConsoleFunctionDelegate Invoke)
{
    /// <summary>Unbounded max-arg marker, for varargs commands like <c>echo(text [, ...])</c>.</summary>
    public const int Unbounded = -1;

    /// <summary><c>Namespace::Name</c> for a method, or just <c>Name</c> for a global function.</summary>
    public string QualifiedName => Namespace is null ? Name : $"{Namespace}::{Name}";

    /// <summary>Whether <paramref name="argCount"/> falls within this entry's declared arity.</summary>
    public bool AcceptsArgCount(int argCount) => argCount >= MinArgs && (MaxArgs == Unbounded || argCount <= MaxArgs);
}
