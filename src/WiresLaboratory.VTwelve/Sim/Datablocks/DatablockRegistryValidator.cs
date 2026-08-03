namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// A class whose <c>parent_class</c> resolves to neither a registered class nor a recognized
/// architectural root — i.e. a name <see cref="DatablockRegistryValidator"/> cannot account for
/// at all. Empty for the TSV this was built against.
/// </summary>
public sealed record DatablockUnresolvedParent(string ClassName, string ParentClassName);

/// <summary>Two fields of the same class whose byte ranges overlap.</summary>
public sealed record DatablockFieldOverlap(
    string ClassName, string FieldA, string FieldB,
    int OffsetA, int EndA, int OffsetB, int EndB);

/// <summary>
/// A child class's own fields reach down into (or below) its direct parent's own field range —
/// the violation <c>ReverseEngineeringNotes.md</c> reports as zero-for-zero across 119 class
/// pairs when the TSV was recovered.
/// </summary>
public sealed record DatablockParentChildOverlap(
    string ChildClass, string ParentClass, int ChildMinOffset, int ParentMaxEndOffset);

/// <summary>
/// Result of running every check in <see cref="DatablockRegistryValidator.Validate"/> over a
/// loaded <see cref="DatablockFieldRegistry"/>. All four lists are expected to be empty for the
/// committed TSV — see <see cref="IsClean"/> — but nothing here assumes that; the checks are
/// real regression tests, not a formality.
/// </summary>
public sealed record DatablockValidationReport(
    IReadOnlyList<DatablockUnresolvedParent> UnresolvedParents,
    IReadOnlyList<DatablockFieldOverlap> SelfOverlaps,
    IReadOnlyList<DatablockParentChildOverlap> ParentChildOverlaps,
    int ParentChildPairsChecked,
    IReadOnlyList<DatablockTypeCodeSizeConflict> TypeSizeConflicts,
    IReadOnlyList<DatablockTypeLabelSizeMismatch> TypeLabelSizeMismatches)
{
    /// <summary>True when every check found nothing.</summary>
    public bool IsClean =>
        UnresolvedParents.Count == 0 && SelfOverlaps.Count == 0 && ParentChildOverlaps.Count == 0
        && TypeSizeConflicts.Count == 0 && TypeLabelSizeMismatches.Count == 0;
}

/// <summary>
/// Self-checks for a loaded <see cref="DatablockFieldRegistry"/>, per the four invariants
/// <c>ReverseEngineeringNotes.md</c> and <c>EngineClassLayout.md</c> record as having held
/// across the whole recovered TSV: every parent resolves, no class's own fields overlap each
/// other, no class's fields overlap its parent's, and the type catalog is internally
/// consistent. These are regression tests against the recovered data, not assumptions about it.
/// </summary>
public static class DatablockRegistryValidator
{
    /// <summary>
    /// Architectural base classes known (from <c>EngineClassLayout.md</c> and general
    /// Torque-lineage <c>GuiControl</c> naming) to register no persist fields of their own, so a
    /// class parented to one of these is not a data-quality problem even though the parent has
    /// no <see cref="DatablockFieldRegistry.Classes"/> entry. Cross-checked against, not
    /// substituted for, <see cref="DatablockFieldRegistry.ImpliedRootClassNames"/> — see
    /// <see cref="Validate"/>.
    /// </summary>
    private static readonly HashSet<string> KnownArchitecturalRoots = new(StringComparer.Ordinal)
    {
        "SimObject", "NetObject", "SimGroup", "ShapeBase", "StaticShape", "MissionMarker", "SimDataBlock",
        "GuiArrayCtrl", "GuiCheckBoxCtrl", "GuiProgressCtrl", "GuiTSCtrl",
    };

    public static DatablockValidationReport Validate(DatablockFieldRegistry registry)
    {
        var unresolvedParents = new List<DatablockUnresolvedParent>();
        foreach (var root in registry.ImpliedRootClassNames)
        {
            if (!KnownArchitecturalRoots.Contains(root))
            {
                // Not itself proof of an error — an implied root is only "unresolved" when it's
                // also outside the recognized set, meaning this loader doesn't know what it is.
                foreach (var def in registry.Classes.Values.Where(d => d.ParentClassName == root))
                    unresolvedParents.Add(new DatablockUnresolvedParent(def.Name, root));
            }
        }

        var selfOverlaps = new List<DatablockFieldOverlap>();
        foreach (var classDef in registry.Classes.Values)
        {
            var fields = classDef.OwnFields;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].EndOffsetBytesExclusive is not int endI) continue;
                for (var j = i + 1; j < fields.Count; j++)
                {
                    if (fields[j].EndOffsetBytesExclusive is not int endJ) continue;
                    if (fields[i].OffsetBytes < endJ && fields[j].OffsetBytes < endI)
                    {
                        selfOverlaps.Add(new DatablockFieldOverlap(
                            classDef.Name, fields[i].FieldName, fields[j].FieldName,
                            fields[i].OffsetBytes, endI, fields[j].OffsetBytes, endJ));
                    }
                }
            }
        }

        var parentChildOverlaps = new List<DatablockParentChildOverlap>();
        var pairsChecked = 0;
        foreach (var classDef in registry.Classes.Values)
        {
            if (!registry.Classes.TryGetValue(classDef.ParentClassName, out var parentDef)) continue;
            if (classDef.OwnFields.Count == 0 || parentDef.OwnFields.Count == 0) continue;

            var parentEnds = parentDef.OwnFields
                .Select(f => f.EndOffsetBytesExclusive)
                .Where(end => end is not null)
                .Select(end => end!.Value)
                .ToArray();
            var childOffsets = classDef.OwnFields.Select(f => f.OffsetBytes).ToArray();
            if (parentEnds.Length == 0 || childOffsets.Length == 0) continue;

            pairsChecked++;
            var parentMaxEnd = parentEnds.Max();
            var childMinOffset = childOffsets.Min();
            if (childMinOffset < parentMaxEnd)
            {
                parentChildOverlaps.Add(new DatablockParentChildOverlap(
                    classDef.Name, parentDef.Name, childMinOffset, parentMaxEnd));
            }
        }

        return new DatablockValidationReport(
            unresolvedParents, selfOverlaps, parentChildOverlaps, pairsChecked,
            registry.TypeSizeConflicts, registry.TypeLabelSizeMismatches);
    }
}
