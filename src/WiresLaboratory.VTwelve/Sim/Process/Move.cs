namespace WiresLaboratory.VTwelve.Sim.Process;

/// <summary>
/// One tick's worth of client input, as delivered to <see cref="IProcessTickable.ProcessTick"/>
/// for a connection's control object.
/// </summary>
/// <remarks>
/// <para>
/// <b>Evidence:</b> <c>Sim/EngineTickLoop.md</c> ("Player movement" / "`ProcessList::advanceObjects`")
/// records the control-object dispatch as <c>obj-&gt;[+0xd8](moveList + (i &lt;&lt; 6))</c> — a shift
/// of 6 places between successive move records, which fixes the size of one <c>Move</c> at
/// **64 bytes** in the shipped binary. That is the only fact about <c>Move</c>'s representation
/// the disassembly gives up.
/// </para>
/// <para>
/// <b>Not recovered:</b> the field layout (movement axes, view angles, trigger/button bits,
/// etc.) has not been recovered — nothing in the tick-loop disassembly touches individual fields
/// of a move, only the record stride. Per the task brief this type is <em>not required to match
/// the 64-byte wire layout</em>; it exists as a distinct, deliberately opaque type so the tick
/// loop's plumbing (dispatch, list handling, per-move iteration) can be built and unit-tested
/// without waiting on that recovery. <see cref="WireSizeBytes"/> records the one dimension that
/// *is* evidenced, for when the real layout is recovered and this type is filled in.
/// </para>
/// </remarks>
public sealed class Move
{
    /// <summary>
    /// The record size the <c>i &lt;&lt; 6</c> move-list indexing in <c>advanceObjects</c>
    /// implies. This is evidence about the original binary's wire/memory layout, not a
    /// constraint this managed type currently honours — see the type remarks.
    /// </summary>
    public const int WireSizeBytes = 64;
}
