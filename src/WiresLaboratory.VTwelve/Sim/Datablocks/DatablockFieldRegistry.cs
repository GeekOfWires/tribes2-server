using System.Reflection;

namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// Raised when a data row in <c>RecoveredDatablockFields.tsv</c> doesn't parse — wrong column
/// count, or a numeric column that isn't. The TSV is the source of truth for field data (see
/// the project's standing rule against hand-transcribing it into C#), so a malformed row is
/// treated as a loader bug or a TSV regression to fix, not something to silently skip.
/// </summary>
public sealed class DatablockFieldRegistryException(string message) : Exception(message);

/// <summary>
/// In-memory registry of every class and field recovered in
/// <c>Sim/RecoveredDatablockFields.tsv</c> — 1,415 fields across 156 classes as of the recovery
/// this loads (<see cref="Summarize"/> reports the live counts). Built by <see cref="Load"/> or
/// <see cref="LoadEmbedded"/>; never hand-populated, per the project rule that the TSV is the
/// only source of field data.
/// </summary>
public sealed class DatablockFieldRegistry
{
    private readonly Dictionary<string, DatablockClassDefinition> _classesByName;

    private DatablockFieldRegistry(
        Dictionary<string, DatablockClassDefinition> classesByName,
        IReadOnlyList<DatablockFieldDescriptor> allFields,
        IReadOnlyDictionary<int, DatablockFieldTypeInfo> types,
        IReadOnlyList<DatablockTypeCodeSizeConflict> typeSizeConflicts,
        IReadOnlyList<DatablockTypeLabelSizeMismatch> typeLabelSizeMismatches)
    {
        _classesByName = classesByName;
        AllFields = allFields;
        Types = types;
        TypeSizeConflicts = typeSizeConflicts;
        TypeLabelSizeMismatches = typeLabelSizeMismatches;
    }

    /// <summary>Every class keyed by name, in the case the TSV itself uses.</summary>
    public IReadOnlyDictionary<string, DatablockClassDefinition> Classes => _classesByName;

    /// <summary>Every field row loaded, across all classes, in TSV order.</summary>
    public IReadOnlyList<DatablockFieldDescriptor> AllFields { get; }

    /// <summary>Type-code metadata built from <see cref="AllFields"/>. See <see cref="DatablockFieldTypeCatalog"/>.</summary>
    public IReadOnlyDictionary<int, DatablockFieldTypeInfo> Types { get; }

    /// <summary>
    /// Type codes whose rows disagree with each other on <c>type_size_bytes</c>. Empty for the
    /// TSV this was built against — kept as a live check rather than an assumption baked into
    /// the loader.
    /// </summary>
    public IReadOnlyList<DatablockTypeCodeSizeConflict> TypeSizeConflicts { get; }

    /// <summary>
    /// Type-code labels whose own parenthesized byte count disagrees with the TSV's
    /// <c>type_size_bytes</c> column for that row — see
    /// <see cref="DatablockFieldTypeCatalog.Build"/>. As of the TSV this was built against,
    /// exactly one: type code 3, label <c>"bool(4)"</c> against a recorded size of 1.
    /// </summary>
    public IReadOnlyList<DatablockTypeLabelSizeMismatch> TypeLabelSizeMismatches { get; }

    /// <summary>
    /// Parent-class names that never appear as a <see cref="Classes"/> key themselves — i.e.
    /// classes with no <c>initPersistFields</c> row of their own in this TSV. Computed
    /// structurally from the loaded data, not a hand-maintained list. Includes both genuine
    /// engine roots that register nothing new (<c>SimDataBlock</c>) and behavioural base
    /// classes reused as a datablock/GUI parent without fields of their own
    /// (<c>ShapeBase</c>, <c>StaticShape</c>, <c>MissionMarker</c>, <c>SimObject</c>,
    /// <c>NetObject</c>, <c>SimGroup</c>, and four <c>Gui*Ctrl</c> bases).
    /// </summary>
    public IReadOnlyCollection<string> ImpliedRootClassNames { get; private set; } = Array.Empty<string>();

    /// <summary>This class's own fields. Empty if <paramref name="className"/> isn't in <see cref="Classes"/>.</summary>
    public IReadOnlyList<DatablockFieldDescriptor> GetOwnFields(string className) =>
        _classesByName.TryGetValue(className, out var def) ? def.OwnFields : Array.Empty<DatablockFieldDescriptor>();

    /// <summary>
    /// Fields inherited from ancestors — walks <see cref="DatablockClassDefinition.ParentClassName"/>
    /// until it reaches a name not present in <see cref="Classes"/> (an <see
    /// cref="ImpliedRootClassNames"/> entry), ordered from the nearest parent's fields to the
    /// most distant ancestor's. Does not include <paramref name="className"/>'s own fields — see
    /// <see cref="GetAllFields"/> for the combined view.
    /// </summary>
    public IReadOnlyList<DatablockFieldDescriptor> GetInheritedFields(string className)
    {
        var result = new List<DatablockFieldDescriptor>();
        var seen = new HashSet<string> { className }; // guards a malformed cycle in the data
        var current = className;
        while (_classesByName.TryGetValue(current, out var def))
        {
            if (!seen.Add(def.ParentClassName)) break;
            if (!_classesByName.TryGetValue(def.ParentClassName, out var parentDef)) break;
            result.AddRange(parentDef.OwnFields);
            current = def.ParentClassName;
        }
        return result;
    }

    /// <summary>
    /// This class's own fields followed by every inherited field (nearest ancestor first) — the
    /// full field set a <see cref="DatablockInstance"/> of this class holds values for.
    /// </summary>
    public IReadOnlyList<DatablockFieldDescriptor> GetAllFields(string className)
    {
        var own = GetOwnFields(className);
        var inherited = GetInheritedFields(className);
        if (inherited.Count == 0) return own;
        var combined = new List<DatablockFieldDescriptor>(own.Count + inherited.Count);
        combined.AddRange(own);
        combined.AddRange(inherited);
        return combined;
    }

    /// <summary>Resolves the direct parent's <see cref="DatablockClassDefinition"/>, or <see langword="null"/> for an implied root.</summary>
    public DatablockClassDefinition? GetParent(string className) =>
        _classesByName.TryGetValue(className, out var def) && _classesByName.TryGetValue(def.ParentClassName, out var parent)
            ? parent
            : null;

    /// <summary>Parses <c>RecoveredDatablockFields.tsv</c>-formatted text into a registry.</summary>
    public static DatablockFieldRegistry Load(TextReader reader)
    {
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null) lines.Add(line);
        return Load(lines);
    }

    /// <summary>Parses a TSV file at <paramref name="path"/> into a registry.</summary>
    public static DatablockFieldRegistry Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return Load(reader);
    }

    /// <summary>
    /// Loads the TSV embedded in this assembly (see the <c>EmbeddedResource</c> entry in
    /// <c>WiresLaboratory.VTwelve.csproj</c> for <c>Sim/RecoveredDatablockFields.tsv</c>) rather
    /// than reading it off disk, so callers don't depend on the working directory or the
    /// source-tree layout at runtime.
    /// </summary>
    public static DatablockFieldRegistry LoadEmbedded()
    {
        var assembly = typeof(DatablockFieldRegistry).Assembly;
        var resourceName = Array.Find(assembly.GetManifestResourceNames(),
            name => name.EndsWith("RecoveredDatablockFields.tsv", StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new DatablockFieldRegistryException(
                "RecoveredDatablockFields.tsv is not embedded in this assembly. " +
                "Check the EmbeddedResource entry in WiresLaboratory.VTwelve.csproj.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new DatablockFieldRegistryException($"Embedded resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return Load(reader);
    }

    private static DatablockFieldRegistry Load(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) throw new DatablockFieldRegistryException("TSV is empty — expected a header row.");

        var fields = new List<DatablockFieldDescriptor>(lines.Count - 1);
        var parentByClass = new Dictionary<string, string>();

        for (var i = 1; i < lines.Count; i++) // row 0 is the header
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            var columns = line.Split('\t');
            if (columns.Length != 14)
            {
                throw new DatablockFieldRegistryException(
                    $"Row {i + 1}: expected 14 tab-separated columns, found {columns.Length}.");
            }

            var className = columns[0];
            var parentClassName = columns[1];

            if (parentByClass.TryGetValue(className, out var knownParent) && knownParent != parentClassName)
            {
                throw new DatablockFieldRegistryException(
                    $"Row {i + 1}: class '{className}' has parent '{parentClassName}' here but " +
                    $"'{knownParent}' earlier in the file — parent_class should be constant per class.");
            }
            parentByClass[className] = parentClassName;

            fields.Add(new DatablockFieldDescriptor(
                ClassName: className,
                ParentClassName: parentClassName,
                FieldName: columns[2],
                TypeCode: ParseInt(columns[3], i, "type_code"),
                TypeSizeBytes: ParseOptionalInt(columns[4]),
                TypeGuessLabel: columns[5],
                OffsetBytes: ParseInt(columns[6], i, "offset_dec"),
                ElementCount: ParseInt(columns[8], i, "elem_count"),
                EnumTableAddress: NullIfEmpty(columns[9]),
                RegistrarFunctionAddress: NullIfEmpty(columns[10]),
                ParentRegistrarFunctionAddress: NullIfEmpty(columns[11]),
                CallSiteAddress: NullIfEmpty(columns[12]),
                ClassConfidence: columns[13]));
        }

        // Type code 9 is the only code that ever populates enum_table, and it always does —
        // the structural lock ReverseEngineeringNotes.md documents. Re-verified here on every
        // load rather than trusted, since it's what the attribution's certainty rests on.
        foreach (var field in fields)
        {
            var hasEnumTable = field.EnumTableAddress is not null;
            if (hasEnumTable != field.IsEnum)
            {
                throw new DatablockFieldRegistryException(
                    $"{field.ClassName}.{field.FieldName}: enum_table population disagrees with " +
                    $"type_code (enum_table {(hasEnumTable ? "set" : "empty")}, type_code {field.TypeCode}). " +
                    "This breaks the structural lock ReverseEngineeringNotes.md relies on for class attribution.");
            }
        }

        var classesByName = fields
            .GroupBy(f => f.ClassName)
            .ToDictionary(
                g => g.Key,
                g => new DatablockClassDefinition(g.Key, g.First().ParentClassName, g.ToList()));

        var (types, sizeConflicts, labelMismatches) = DatablockFieldTypeCatalog.Build(fields);

        var impliedRoots = parentByClass.Values
            .Where(parent => !classesByName.ContainsKey(parent))
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return new DatablockFieldRegistry(classesByName, fields, types, sizeConflicts, labelMismatches)
        {
            ImpliedRootClassNames = impliedRoots,
        };
    }

    private static int ParseInt(string value, int rowIndex, string columnName) =>
        int.TryParse(value, out var parsed)
            ? parsed
            : throw new DatablockFieldRegistryException($"Row {rowIndex + 1}: '{columnName}' column value '{value}' is not an integer.");

    private static int? ParseOptionalInt(string value) => value.Length == 0 ? null : int.Parse(value);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
