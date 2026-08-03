namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// How firmly <see cref="DatablockFieldTypeInfo"/> claims to know a type code's shape.
/// </summary>
/// <remarks>
/// Derived mechanically from the TSV's own notation, not asserted per type code by hand: every
/// <c>type_guess</c> label that the recovery authors were unsure of is itself written with a
/// trailing <c>?</c> (<c>"S32?"</c>, <c>"Point3F?"</c>, <c>"AudioEnvironment*?"</c>, or a bare
/// <c>"?"</c>). A type code is <see cref="Unconfirmed"/> if and only if its label carries that
/// mark; see <see cref="DatablockFieldTypeCatalog"/>.
/// </remarks>
public enum DatablockFieldConfidence
{
    /// <summary>The recovery authors wrote a plain label with no <c>?</c>.</summary>
    Confirmed,

    /// <summary>The label itself carries a <c>?</c> — the recovery authors flagged doubt.</summary>
    Unconfirmed,
}

/// <summary>
/// Metadata for one <c>addField</c> type code, built by <see cref="DatablockFieldTypeCatalog"/>
/// from the rows that actually use it rather than hand-authored per code.
/// </summary>
/// <param name="TypeCode">The raw code passed as <c>addField</c>'s second argument.</param>
/// <param name="Label">
/// The <c>type_guess</c> label verbatim, taken from the first row observed for this code (every
/// row for a given code agrees — see <see cref="DatablockFieldTypeCatalog"/>'s consistency
/// check).
/// </param>
/// <param name="ElementSizeBytes">
/// Per-element size in bytes from the TSV's <c>type_size_bytes</c> column, or <see
/// langword="null"/> for the handful of codes the recovery left that column blank (types 6, 20,
/// 35, 38, 43 — 8 rows total). A field's total byte span is <c>ElementSizeBytes * ElementCount</c>
/// per row, exactly as <c>ReverseEngineeringNotes.md</c>'s overlap cross-check computes it.
/// </param>
/// <param name="ClrType">
/// A best-effort managed type for a single element, or <see langword="null"/> when no
/// reasonable mapping exists yet — either because the code is <see
/// cref="DatablockFieldConfidence.Unconfirmed"/>, or because it is a datablock/profile pointer
/// (<see cref="IsReference"/>) that would need a live name-to-instance directory to resolve,
/// which this static model does not have.
/// </param>
/// <param name="IsEnum">Type code 9 — the sole code that ever populates <c>enum_table</c>.</param>
/// <param name="IsReference">
/// True when the label (after trimming a trailing <c>?</c>) ends in <c>*</c> and the code isn't
/// 7 (<c>char*</c>, modeled as <see cref="string"/> instead) — i.e. a datablock/profile pointer
/// field such as <c>DataBlock*</c>, <c>ExplosionData*</c>, <c>AudioProfile*</c>.
/// </param>
public sealed record DatablockFieldTypeInfo(
    int TypeCode,
    string Label,
    int? ElementSizeBytes,
    Type? ClrType,
    bool IsEnum,
    bool IsReference,
    DatablockFieldConfidence Confidence);
