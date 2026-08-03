namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// One class that registered persistent fields via <c>initPersistFields</c> — most, but not
/// all, are <c>SimDataBlock</c>-lineage datablocks; the same registrar and the same TSV also
/// captured plain <c>SimObject</c>/<c>GuiControl</c> field registrations (e.g.
/// <c>TerrainBlock</c>, <c>AudioEmitter</c>, <c>GuiSliderCtrl</c>) since they go through the
/// identical <c>addField</c> call. "Datablock" in this model's name reflects the primary use
/// case, not a hard restriction on what <see cref="DatablockFieldRegistry"/> will load.
/// </summary>
/// <param name="Name">The class name, e.g. <c>PlayerData</c>.</param>
/// <param name="ParentClassName">
/// The parent this class's <c>init()</c> chains to. May name a class that itself registers no
/// fields (e.g. <c>ShapeBaseData -&gt; GameBaseData</c> resolves, but
/// <c>GameBaseData -&gt; SimDataBlock</c>'s <c>SimDataBlock</c> has no row of its own — it is a
/// documented root; see <see cref="DatablockRegistryValidator"/>).
/// </param>
/// <param name="OwnFields">
/// Fields this class registers itself, in TSV row order. Does not include inherited fields —
/// see <see cref="DatablockFieldRegistry.GetInheritedFields"/> and
/// <see cref="DatablockFieldRegistry.GetAllFields"/> for the walked view.
/// </param>
public sealed record DatablockClassDefinition(
    string Name,
    string ParentClassName,
    IReadOnlyList<DatablockFieldDescriptor> OwnFields);
