namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// One row of <c>RecoveredDatablockFields.tsv</c> — one <c>addField(name, typeCode, offset,
/// elementCount, EnumTable*)</c> call site recovered from the shipped binary, plus the
/// class/parent attribution <c>ReverseEngineeringNotes.md</c> describes deriving from the
/// vtable's <c>init()</c> slot. This is metadata only: an offset and size describe where the
/// field lives inside the native object layout the engine used, not a managed field this
/// project reads or writes directly — see <see cref="DatablockInstance"/> for the
/// value-holding side.
/// </summary>
/// <param name="ClassName">The registering class, e.g. <c>PlayerData</c>.</param>
/// <param name="ParentClassName">
/// The class whose <c>initPersistFields</c> this class's <c>init()</c> chains to. Every row for
/// a given <see cref="ClassName"/> agrees on this value in the source TSV (checked by
/// <see cref="DatablockFieldRegistry"/> while loading) — it is a class-level fact that happens
/// to be repeated per field in the row format.
/// </param>
/// <param name="FieldName">The field name passed as <c>addField</c>'s first argument.</param>
/// <param name="TypeCode">The raw <c>type_code</c> column. See <see cref="DatablockFieldTypeCatalog"/>.</param>
/// <param name="TypeSizeBytes">
/// Per-element size in bytes, derived by the recovery from consecutive offset deltas. Null for
/// the 8 rows the recovery could not pin down (type codes 6, 20, 35, 38, 43).
/// </param>
/// <param name="TypeGuessLabel">The <c>type_guess</c> column verbatim, e.g. <c>"Point3F(12B)"</c>.</param>
/// <param name="OffsetBytes">Byte offset of this field within the native object.</param>
/// <param name="ElementCount">
/// Array length for this field; 1 for a scalar. The field's total byte span is
/// <c>TypeSizeBytes * ElementCount</c> — the same formula
/// <c>ReverseEngineeringNotes.md</c>'s parent/child overlap cross-check uses.
/// </param>
/// <param name="EnumTableAddress">
/// The <c>EnumTable*</c> argument (a <c>.bss</c> address) when non-empty. Populated if and only
/// if <see cref="TypeCode"/> is 9 — the structural lock <c>ReverseEngineeringNotes.md</c>
/// documents and <see cref="DatablockFieldRegistry"/> re-verifies on load.
/// </param>
/// <param name="RegistrarFunctionAddress">The class's <c>initPersistFields</c> address.</param>
/// <param name="ParentRegistrarFunctionAddress">
/// The parent class's <c>initPersistFields</c> address, when the call site named it directly
/// (blank for several rows — the parent link there comes from <see cref="ParentClassName"/>
/// alone).
/// </param>
/// <param name="CallSiteAddress">Address of the specific <c>addField</c> call.</param>
/// <param name="ClassConfidence">
/// The <c>class_confidence</c> column: <c>vtable-init</c> for a field attributed directly from
/// the vtable walk, plus a few qualified variants (<c>ambiguous:...</c>,
/// <c>inlined-into-init</c>, <c>inferred-by-elimination</c>) recording how attribution was
/// resolved when it wasn't a direct match.
/// </param>
public sealed record DatablockFieldDescriptor(
    string ClassName,
    string ParentClassName,
    string FieldName,
    int TypeCode,
    int? TypeSizeBytes,
    string TypeGuessLabel,
    int OffsetBytes,
    int ElementCount,
    string? EnumTableAddress,
    string? RegistrarFunctionAddress,
    string? ParentRegistrarFunctionAddress,
    string? CallSiteAddress,
    string ClassConfidence)
{
    /// <summary>Type code 9 — see <see cref="DatablockFieldTypeInfo.IsEnum"/>.</summary>
    public bool IsEnum => TypeCode == 9;

    /// <summary>
    /// Byte offset one past the end of this field (<see cref="OffsetBytes"/> +
    /// <see cref="TypeSizeBytes"/> * <see cref="ElementCount"/>), or <see langword="null"/> when
    /// <see cref="TypeSizeBytes"/> is unknown and no span can be computed. This is the exact
    /// quantity <see cref="DatablockRegistryValidator"/> uses for overlap checks.
    /// </summary>
    public int? EndOffsetBytesExclusive => TypeSizeBytes is int size ? OffsetBytes + size * ElementCount : null;

    /// <summary>
    /// Whether this field's byte range overlaps <paramref name="other"/>'s. False (not "unknown")
    /// when either field's span can't be computed — callers that need to distinguish "known
    /// disjoint" from "unknown" should check <see cref="EndOffsetBytesExclusive"/> first, which
    /// is exactly what <see cref="DatablockRegistryValidator"/> does before calling this.
    /// </summary>
    public bool Overlaps(DatablockFieldDescriptor other)
    {
        if (EndOffsetBytesExclusive is not int endA || other.EndOffsetBytesExclusive is not int endB) return false;
        return OffsetBytes < endB && other.OffsetBytes < endA;
    }
}
