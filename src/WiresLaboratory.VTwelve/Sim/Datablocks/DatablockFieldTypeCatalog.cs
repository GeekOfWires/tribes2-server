using System.Text.RegularExpressions;

namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// Two rows that name the same <c>type_guess</c> label but disagree on
/// <c>type_size_bytes</c> for that type code — a self-consistency failure within the TSV
/// itself, independent of any external knowledge of the engine's real layout.
/// </summary>
public sealed record DatablockTypeCodeSizeConflict(int TypeCode, int FirstSizeBytes, int ConflictingSizeBytes, string ObservedOnClassField);

/// <summary>
/// A <c>type_guess</c> label that embeds its own expected byte count in parentheses (e.g.
/// <c>"ColorF(16B)"</c>, <c>"bool(4)"</c>) but disagrees with the TSV's own
/// <c>type_size_bytes</c> column for that row.
/// </summary>
public sealed record DatablockTypeLabelSizeMismatch(int TypeCode, string Label, int LabelStatedSize, int ColumnSize);

/// <summary>
/// Builds <see cref="DatablockFieldTypeInfo"/> for every type code that actually appears in
/// <c>RecoveredDatablockFields.tsv</c>, deriving as much as possible from the rows themselves
/// rather than hand-authoring a 34-entry table blind. See <see cref="Build"/>.
/// </summary>
public static class DatablockFieldTypeCatalog
{
    // A label's own parenthesized byte count, e.g. "ColorF(16B)" -> 16, "bool(4)" -> 4.
    // Matched against type_size_bytes as a purely internal TSV cross-check (see
    // DatablockTypeLabelSizeMismatch) — no outside claim about the engine's real layout.
    private static readonly Regex LabelSizeSuffix = new(@"\((\d+)B?\)", RegexOptions.Compiled);

    /// <summary>
    /// One managed CLR type per <em>confirmed</em>, fixed-layout type code. Left out entirely
    /// for every code that is <see cref="DatablockFieldConfidence.Unconfirmed"/> (label carries
    /// a <c>?</c>) or that is a datablock/profile pointer (<see
    /// cref="DatablockFieldTypeInfo.IsReference"/>) — those get <see langword="null"/> from
    /// <see cref="Build"/> rather than a guessed shape. This is the one place actual domain
    /// knowledge (which struct fields correspond to which type code) enters the model; the size
    /// used at runtime always still comes from the TSV row, not from these structs' own
    /// <see langword="sizeof"/>.
    /// </summary>
    private static readonly Dictionary<int, Type> ConfirmedClrTypes = new()
    {
        [1] = typeof(int),                      // S32
        [3] = typeof(bool),                     // bool(4) — see the size-mismatch note below
        [5] = typeof(float),                    // F32
        [7] = typeof(string),                   // string(char*)
        [8] = typeof(string),                   // string-ptr (StringTable entry)
        [9] = typeof(int),                      // enum(S32)+table — raw ordinal, see IsEnum
        [11] = typeof(DatablockColorI),
        [12] = typeof(DatablockColorF),
        [14] = typeof(DatablockPoint2I),
        [16] = typeof(DatablockPoint3F),
        [18] = typeof(DatablockRectI),
    };

    /// <summary>
    /// Scans every field row, groups by <c>type_code</c>, and produces one
    /// <see cref="DatablockFieldTypeInfo"/> per code plus whatever internal inconsistencies the
    /// scan turns up. Confidence, size and reference-ness are all derived from the label text
    /// and size column as documented on <see cref="DatablockFieldTypeInfo"/> — nothing here is
    /// a per-code hand-authored guess except the CLR type for the eleven codes in
    /// <see cref="ConfirmedClrTypes"/>.
    /// </summary>
    public static (IReadOnlyDictionary<int, DatablockFieldTypeInfo> Types,
        IReadOnlyList<DatablockTypeCodeSizeConflict> SizeConflicts,
        IReadOnlyList<DatablockTypeLabelSizeMismatch> LabelSizeMismatches)
        Build(IEnumerable<DatablockFieldDescriptor> rows)
    {
        var byCode = new Dictionary<int, (string Label, int? Size, string SampleClassField)>();
        var sizeConflicts = new List<DatablockTypeCodeSizeConflict>();
        var labelMismatches = new List<DatablockTypeLabelSizeMismatch>();
        var reportedLabelMismatch = new HashSet<int>();

        foreach (var row in rows)
        {
            if (byCode.TryGetValue(row.TypeCode, out var existing))
            {
                if (existing.Size is int knownSize && row.TypeSizeBytes is int rowSize && knownSize != rowSize)
                {
                    sizeConflicts.Add(new DatablockTypeCodeSizeConflict(
                        row.TypeCode, knownSize, rowSize, $"{row.ClassName}.{row.FieldName}"));
                }
            }
            else
            {
                byCode[row.TypeCode] = (row.TypeGuessLabel, row.TypeSizeBytes, $"{row.ClassName}.{row.FieldName}");
            }

            var suffixMatch = LabelSizeSuffix.Match(row.TypeGuessLabel);
            if (suffixMatch.Success && row.TypeSizeBytes is int actualSize
                && int.Parse(suffixMatch.Groups[1].Value) != actualSize
                && reportedLabelMismatch.Add(row.TypeCode))
            {
                labelMismatches.Add(new DatablockTypeLabelSizeMismatch(
                    row.TypeCode, row.TypeGuessLabel, int.Parse(suffixMatch.Groups[1].Value), actualSize));
            }
        }

        var types = new Dictionary<int, DatablockFieldTypeInfo>();
        foreach (var (code, info) in byCode)
        {
            var trimmedLabel = info.Label.TrimEnd('?');
            var isUnconfirmed = info.Label.Contains('?') || info.Label.Length == 0;
            var isReference = code != 7 && trimmedLabel.EndsWith('*');
            var isEnum = code == 9;

            ConfirmedClrTypes.TryGetValue(code, out var clrType);
            if (isUnconfirmed || isReference)
                clrType = null; // never guess a shape for these — see the ClrType doc comment.

            types[code] = new DatablockFieldTypeInfo(
                TypeCode: code,
                Label: info.Label,
                ElementSizeBytes: info.Size,
                ClrType: clrType,
                IsEnum: isEnum,
                IsReference: isReference,
                Confidence: isUnconfirmed ? DatablockFieldConfidence.Unconfirmed : DatablockFieldConfidence.Confirmed);
        }

        return (types, sizeConflicts, labelMismatches);
    }
}
