namespace WiresLaboratory.VTwelve.Sim.Process;

/// <summary>
/// A simulation object that participates in <see cref="ProcessList"/>'s tick loop — the managed
/// counterpart of a <c>GameBase</c> linked into <c>gServerProcessList</c> /
/// <c>gClientProcessList</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Evidence:</b> the three methods correspond to confirmed vtable slots recorded in
/// <c>Sim/EngineClassLayout.md</c> — slot 54 <c>processTick(const Move*)</c>, slot 55
/// <c>interpolateTick(F32 delta)</c>, slot 56 <c>advanceTime(F32 dt)</c> — and
/// <see cref="ProcessTickEnabled"/> / <see cref="AdvanceTimeEnabled"/> correspond to the enable
/// flags <c>Sim/EngineTickLoop.md</c> records at instance offsets <c>+0x264</c> /
/// <c>+0x265</c> respectively.
/// </para>
/// <para>
/// <b>Ambiguity between the two source docs:</b> <c>EngineClassLayout.md</c>'s own field table
/// lists only <c>+0x265</c>, labelled generically as "GameBase tick flag" — it does not
/// separately corroborate <c>+0x264</c>. <c>EngineTickLoop.md</c> is the more specific and more
/// recently recovered source for the tick loop itself and explicitly names both flags and what
/// gates what, so it is treated as authoritative here; the two are not necessarily in conflict
/// (a generic "tick flag" label is consistent with it being the advance-time enable), but this
/// has not been independently reconciled.
/// </para>
/// <para>
/// <b>Not evidenced:</b> nothing in either doc states that <see cref="InterpolateTick"/> is
/// gated by an enable flag the way <c>processTick</c> and <c>advanceTime</c> are — only two
/// flags were recovered, and the tick-loop doc's per-object description of the interpolate pass
/// says only that it is "a further full pass" with no mention of a gate. <see cref="ProcessList"/>
/// therefore calls <see cref="InterpolateTick"/> unconditionally for every object in the list;
/// if a third enable flag exists it has not been recovered.
/// </para>
/// <para>
/// <b>Not evidenced (our own modelling choice):</b> <see cref="ControllingConnection"/> is not a
/// recovered field — it is how this re-implementation locates "the connection's control object"
/// per <see cref="ProcessList"/>'s per-object dispatch rule. The doc confirms that rule exists
/// structurally (a distinct move-dispatch code path keyed off the object being a connection's
/// control object) but not the mechanism the original engine uses to recognise it from inside
/// the object loop (candidate back-pointer offsets <c>+0x2d0</c>/<c>+0x2d4</c> are themselves
/// flagged as an unresolved conflict in <c>EngineTickLoop.md</c>).
/// </para>
/// </remarks>
public interface IProcessTickable
{
    /// <summary>
    /// Gates the <see cref="ProcessTick"/> dispatch — the managed equivalent of the enable flag
    /// at instance offset <c>+0x264</c>. When <c>false</c>, <see cref="ProcessList"/> skips this
    /// object entirely for the fixed-step tick pass (it is still walked, just not dispatched —
    /// see the link-shuffle notes on <see cref="ProcessList"/>).
    /// </summary>
    bool ProcessTickEnabled { get; }

    /// <summary>
    /// Gates the <see cref="AdvanceTime"/> dispatch on the client-side "advance" pass — the
    /// managed equivalent of the enable flag at instance offset <c>+0x265</c>.
    /// </summary>
    bool AdvanceTimeEnabled { get; }

    /// <summary>
    /// If this object is a connection's control object, the connection whose queued
    /// <see cref="Move"/>s should drive it this tick. <c>null</c> for every other object, which
    /// instead receives a single <see cref="ProcessTick"/> call with a <c>null</c> move. See the
    /// type remarks — this property is this implementation's chosen mechanism for a dispatch
    /// rule the doc evidences only structurally.
    /// </summary>
    IMoveSource? ControllingConnection { get; }

    /// <summary>
    /// The fixed-step simulation tick — vtable slot 54. Called once per queued move for a
    /// control object (<paramref name="move"/> non-null, in queue order), or once with
    /// <paramref name="move"/> null for every other ticked object. Never receives a delta: the
    /// timestep is always exactly <see cref="ProcessList.TickMilliseconds"/> by construction (a
    /// fixed-rate simulation is the entire point of this dispatch — see
    /// <c>Sim/EngineTickLoop.md</c>, "Tick rate: 32 ms").
    /// </summary>
    void ProcessTick(Move? move);

    /// <summary>
    /// Client-side render-time interpolation — vtable slot 55. Receives the fractional remainder
    /// <c>(32 - (mLastTime &amp; 31)) / 32</c>, <em>not</em> the same value passed to
    /// <see cref="AdvanceTime"/> — see <see cref="ProcessList.AdvanceClientTime"/>.
    /// </summary>
    void InterpolateTick(float delta);

    /// <summary>
    /// Client-side wall-clock advance — vtable slot 56. Receives elapsed time in <b>seconds</b>
    /// (the original converts milliseconds by multiplying by <c>0.001f</c>), not milliseconds
    /// and not the fixed tick step.
    /// </summary>
    void AdvanceTime(float deltaSeconds);
}
