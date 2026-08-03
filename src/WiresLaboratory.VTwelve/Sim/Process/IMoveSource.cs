namespace WiresLaboratory.VTwelve.Sim.Process;

/// <summary>
/// The connection-side move queue a control object's <see cref="IProcessTickable.ProcessTick"/>
/// pass is driven from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Evidence:</b> <c>Sim/EngineTickLoop.md</c> describes the control-object path in
/// <c>ProcessList::advanceObjects</c> as: <c>conn-&gt;[+0xc4](&amp;moves, &amp;count)</c>, then one
/// <c>processTick</c> call per queued move, then <c>conn-&gt;[+0xc8](count)</c> to clear what was
/// consumed. <see cref="FetchMoves"/> and <see cref="ClearMoves"/> stand in for those two
/// <c>GameConnection</c> vtable slots (49/50 per the doc's offset table).
/// </para>
/// <para>
/// <b>Inferred, not confirmed:</b> the doc is explicit that slots 49/50/51 "are inferred from
/// usage only — the <c>GameConnection</c> vtable itself was not located by that analysis", and
/// separately flags a conflict between two candidate vtable addresses for <c>GameConnection</c>
/// that has not been reconciled. So the existence and rough shape of a fetch/clear pair is
/// reasonably well evidenced; the exact signatures (e.g. whether the clear count must equal the
/// fetch count, whether fetch can be called more than once per tick) are not.
/// </para>
/// </remarks>
public interface IMoveSource
{
    /// <summary>
    /// The moves currently queued for this tick. <see cref="ProcessList"/> calls
    /// <see cref="IProcessTickable.ProcessTick"/> once per entry, in order.
    /// </summary>
    IReadOnlyList<Move> FetchMoves();

    /// <summary>
    /// Called once after every queued move has been dispatched, with the number that were
    /// consumed — mirroring <c>conn-&gt;[+0xc8](count)</c>. Implementations should drop exactly
    /// that many moves from the front of the queue.
    /// </summary>
    void ClearMoves(int count);
}
