namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// How much of the recovered console surface (EngineConsoleSurface.md, 304 functions across the
/// global namespace and <c>BanList</c>) is currently implemented by a <see cref="ConsoleRegistry"/>.
/// Produced by <see cref="ConsoleRegistry.GetCoverage"/> so this is a live query against actual
/// registrations, not a hand-maintained checklist that drifts from the code.
/// </summary>
public sealed record ConsoleCoverageReport(int TotalExpected, int Implemented, IReadOnlyList<ConsoleManifestEntry> Missing)
{
    public int MissingCount => Missing.Count;

    public double Fraction => TotalExpected == 0 ? 0d : (double)Implemented / TotalExpected;
}
