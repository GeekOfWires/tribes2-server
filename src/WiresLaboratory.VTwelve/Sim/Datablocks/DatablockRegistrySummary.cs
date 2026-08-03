namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>Field and byte-coverage counts for one class, own fields only (see <see cref="DatablockClassDefinition.OwnFields"/>).</summary>
public sealed record DatablockClassCoverage(string ClassName, string ParentClassName, int OwnFieldCount, int InheritedFieldCount);

/// <summary>How many fields, and how many distinct classes, use one type code.</summary>
public sealed record DatablockTypeUsage(int TypeCode, string Label, DatablockFieldConfidence Confidence, int FieldCount, int ClassCount);

/// <summary>
/// Registry-wide counts, queryable rather than printed, so a caller (a report, a test, a
/// console command) can inspect coverage without re-deriving it from
/// <see cref="DatablockFieldRegistry.AllFields"/> by hand.
/// </summary>
public sealed record DatablockRegistrySummary(
    int ClassCount,
    int FieldCount,
    IReadOnlyList<DatablockClassCoverage> ClassCoverage,
    IReadOnlyList<DatablockTypeUsage> TypeUsage)
{
    /// <summary>Builds a summary from a loaded registry.</summary>
    public static DatablockRegistrySummary Build(DatablockFieldRegistry registry)
    {
        var classCoverage = registry.Classes.Values
            .Select(c => new DatablockClassCoverage(
                c.Name, c.ParentClassName, c.OwnFields.Count, registry.GetInheritedFields(c.Name).Count))
            .OrderBy(c => c.ClassName, StringComparer.Ordinal)
            .ToArray();

        var typeUsage = registry.AllFields
            .GroupBy(f => f.TypeCode)
            .Select(g =>
            {
                var info = registry.Types[g.Key];
                return new DatablockTypeUsage(
                    g.Key, info.Label, info.Confidence, g.Count(),
                    g.Select(f => f.ClassName).Distinct().Count());
            })
            .OrderBy(t => t.TypeCode)
            .ToArray();

        return new DatablockRegistrySummary(
            registry.Classes.Count, registry.AllFields.Count, classCoverage, typeUsage);
    }
}
