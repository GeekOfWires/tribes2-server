namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// One expected console command, as read from EngineConsoleSurface.md — a global function
/// (<see cref="Namespace"/> is <see langword="null"/>) or a <c>Class::method</c>. This is what
/// the binary says should exist; <see cref="ConsoleFunctionEntry"/> is what this codebase
/// actually implements. <see cref="ConsoleRegistry.GetCoverage"/> is the bridge between them.
/// </summary>
/// <param name="RawSignature">
/// The manifest line verbatim (e.g. <c>"VectorCross(vec1,vec2)"</c>), kept for diagnostics and
/// for entries <see cref="ConsoleManifest"/> could not cleanly parse into name/args.
/// </param>
public sealed record ConsoleManifestEntry(string? Namespace, string Name, string RawSignature)
{
    public string QualifiedName => Namespace is null ? Name : $"{Namespace}::{Name}";
}
