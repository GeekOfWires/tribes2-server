namespace WiresLaboratory.VTwelve.Sim.Process;

/// <summary>
/// The managed counterpart of <c>ProcessList</c> (<c>gServerProcessList</c> /
/// <c>gClientProcessList</c>): a fixed-32ms-timestep tick loop over a set of
/// <see cref="IProcessTickable"/> objects, safe for objects to add or remove themselves (or
/// others) from mid-pass.
/// </summary>
/// <remarks>
/// <para>
/// See <c>Sim/EngineTickLoop.md</c> for the full recovered chain and its evidence. This type
/// covers the two entry points at the bottom of that chain: <see cref="AdvanceServerTime"/>
/// (<c>ProcessList::advanceServerTime</c>, <c>0x00602350</c>) and <see cref="AdvanceClientTime"/>
/// (<c>ProcessList::advanceClientTime</c>, <c>0x00602430</c>), both driving the shared
/// <c>advanceObjects</c> tick pass (<c>0x00602720</c>).
/// </para>
/// <para>
/// <b>Scope note:</b> <c>GameInterface::processTime</c> — the caller above both of these, which
/// clamps elapsed time to <see cref="ElapsedClampMilliseconds"/> and "applies a timescale"
/// before dispatching to server and client — is not itself reproduced here (it is outside
/// <c>Sim/Process/</c>, and no timescale factor/mechanism was recovered). The elapsed-time clamp
/// is explicitly called out as something to reproduce, so it is applied directly by
/// <see cref="AdvanceServerTime"/> / <see cref="AdvanceClientTime"/> instead — functionally the
/// same bound on catch-up after a stall, just applied one layer lower than in the original.
/// </para>
/// </remarks>
public sealed class ProcessList
{
    /// <summary>
    /// The timestep the shipped engine uses: 32 ms, i.e. 31.25 Hz. Evidenced directly in the
    /// disassembly: <c>and esi, 0xffffffe0</c> / <c>shr ... , 5</c> /
    /// <c>add dword ptr [ebx+0x288], 0x20</c> — TickShift 5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a protocol constant, not a tuning knob.</b> The stock game client runs its own
    /// local movement prediction at this exact timestep. A server ticking at any other rate
    /// predicts different positions from the client that is talking to it, and players see that
    /// as rubber-banding. For any session with an unmodified client this value is the only
    /// correct one.
    /// </para>
    /// </remarks>
    public const uint StockTickMilliseconds = 0x20;

    /// <summary>
    /// The fixed simulation timestep this list actually runs at, in milliseconds. Defaults to
    /// <see cref="StockTickMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Configurable so the simulation can be driven at other rates for purposes that do not
    /// involve a stock client — deterministic replay, accelerated headless tests, profiling, or
    /// a future client that agrees on a different rate. <b>Changing it breaks compatibility with
    /// the stock client</b>; see <see cref="StockTickMilliseconds"/>.
    /// </para>
    /// <para>
    /// Constrained to a power of two because the engine's tick arithmetic is bitwise
    /// (<c>total &amp; ~(tick-1)</c> to find the boundary, <c>&amp; (tick-1)</c> for the sub-tick
    /// remainder). Those are reproduced here exactly rather than replaced with division, so a
    /// non-power-of-two would silently compute the wrong boundary instead of failing.
    /// </para>
    /// </remarks>
    public uint TickMilliseconds { get; }

    /// <summary>Creates a list running at the stock 32 ms timestep.</summary>
    public ProcessList() : this(StockTickMilliseconds)
    {
    }

    /// <summary>
    /// Creates a list running at a given timestep. Prefer the parameterless constructor unless
    /// you specifically intend to diverge from the stock client — see
    /// <see cref="TickMilliseconds"/>.
    /// </summary>
    /// <param name="tickMilliseconds">Timestep in milliseconds; must be a power of two.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is zero or not a power of two, which the bitwise tick arithmetic cannot express.
    /// </exception>
    public ProcessList(uint tickMilliseconds)
    {
        if (tickMilliseconds == 0 || (tickMilliseconds & (tickMilliseconds - 1)) != 0)
            throw new ArgumentOutOfRangeException(
                nameof(tickMilliseconds), tickMilliseconds,
                "The tick timestep must be a non-zero power of two: the engine's tick arithmetic "
                + "is bitwise, so any other value computes the wrong boundary rather than failing.");

        TickMilliseconds = tickMilliseconds;
    }

    /// <summary>
    /// <c>GameInterface::processTime</c> clamps incoming elapsed time to this many milliseconds
    /// before it reaches either process list, bounding how many ticks a single call can ever
    /// catch up after a stall (e.g. a debugger break or a slow frame). See the scope note on
    /// this type for why it is applied here rather than in a separate <c>GameInterface</c> type.
    /// </summary>
    public const uint ElapsedClampMilliseconds = 0x400;

    /// <summary>
    /// Mirrors instance offset <c>+0x288</c>: how much simulation time has actually been
    /// consumed by completed ticks, in milliseconds. Advances in exact
    /// <see cref="TickMilliseconds"/> steps and is always &lt;= <see cref="LastTime"/>.
    /// </summary>
    public uint LastTick { get; private set; }

    /// <summary>
    /// Mirrors instance offset <c>+0x28c</c>: the running total of elapsed milliseconds ever fed
    /// into this list via <see cref="AdvanceServerTime"/>/<see cref="AdvanceClientTime"/>. Unlike
    /// <see cref="LastTick"/> this is never rounded down to a tick boundary, so
    /// <c>LastTime - LastTick</c> is always the sub-tick remainder (0..31 ms).
    /// </summary>
    public uint LastTime { get; private set; }

    /// <summary>
    /// Mirrors instance offset <c>+0x290</c>. <b>Inferred, not directly evidenced:</b> the doc
    /// groups this offset with <see cref="LastTick"/>/<see cref="LastTime"/> as "tick
    /// accounting" but does not state its role. This implementation's working hypothesis — the
    /// only one that supplies a value for the client "advance" pass's <c>delta * 0.001f</c>
    /// (<c>Sim/EngineTickLoop.md</c>, "Client-side passes") without re-deriving it from
    /// <see cref="LastTime"/>/<see cref="LastTick"/> — is that it is simply the raw per-call
    /// elapsed-milliseconds argument, held over for that second pass. If that hypothesis is
    /// wrong, only the value <see cref="AdvanceTime"/> passes changes; the tick-count and
    /// interpolation behaviour do not depend on it.
    /// </summary>
    public uint LastDelta { get; private set; }

    /// <summary>Doubly-linked sentinel. <c>_head.Next == _head</c> when the list is empty.</summary>
    private readonly ProcessNode _head = ProcessNode.CreateSentinel();

    /// <summary>
    /// Reference-identity lookup from a registered object to its node, so
    /// <see cref="Remove"/> can find and unlink it in O(1) — the managed stand-in for the
    /// original's intrusive <c>mProcessLink</c> pointers living directly on the object.
    /// </summary>
    private readonly Dictionary<IProcessTickable, ProcessNode> _nodes = new(ReferenceEqualityComparer.Instance);

    /// <summary>A node in the intrusive-style process list. <see cref="Owner"/> is null only for a sentinel.</summary>
    private sealed class ProcessNode
    {
        public IProcessTickable? Owner;
        public ProcessNode Next = null!;
        public ProcessNode Prev = null!;

        public static ProcessNode CreateSentinel()
        {
            var node = new ProcessNode();
            node.Next = node;
            node.Prev = node;
            return node;
        }
    }

    /// <summary>Number of objects currently registered.</summary>
    public int Count => _nodes.Count;

    /// <summary>
    /// A snapshot of the currently registered objects, in list order. Safe to enumerate while
    /// the list is later mutated — it is a copy, not a live view.
    /// </summary>
    public IReadOnlyList<IProcessTickable> Objects
    {
        get
        {
            var list = new List<IProcessTickable>(_nodes.Count);
            for (var n = _head.Next; n != _head; n = n.Next)
                list.Add(n.Owner!);
            return list;
        }
    }

    /// <summary>
    /// Registers an object at the tail of the process list — mirrors the insert-before-sentinel
    /// link routine at <c>0x5e3150</c>. Safe to call from inside a tick dispatch (see the
    /// link-shuffle notes on <see cref="AdvanceObjects"/>): a newly added object is not visited
    /// by the pass in progress, only by the next one.
    /// </summary>
    public void Add(IProcessTickable obj)
    {
        if (_nodes.ContainsKey(obj))
            throw new ArgumentException("object is already registered in this process list", nameof(obj));

        var node = new ProcessNode { Owner = obj };
        LinkBefore(_head, node);
        _nodes.Add(obj, node);
    }

    /// <summary>
    /// Unregisters an object — mirrors the unlink routine at <c>0x5e3110</c>. Safe to call from
    /// inside that same object's own tick dispatch, or from another object's, regardless of
    /// whether the removed object has already been processed this pass: see the link-shuffle
    /// notes on <see cref="AdvanceObjects"/>. Returns <c>false</c> if <paramref name="obj"/>
    /// was not registered (a no-op, not an error — mirrors self-removal being safe to attempt
    /// even from code paths that cannot easily tell whether it already happened).
    /// </summary>
    public bool Remove(IProcessTickable obj)
    {
        if (!_nodes.Remove(obj, out var node))
            return false;

        Unlink(node);
        return true;
    }

    private static void Unlink(ProcessNode node)
    {
        node.Prev.Next = node.Next;
        node.Next.Prev = node.Prev;
    }

    /// <summary>Inserts <paramref name="node"/> immediately before <paramref name="before"/>.</summary>
    private static void LinkBefore(ProcessNode before, ProcessNode node)
    {
        node.Next = before;
        node.Prev = before.Prev;
        before.Prev.Next = node;
        before.Prev = node;
    }

    /// <summary>
    /// The link-shuffle idiom from <c>ProcessList::advanceObjects</c> (<c>0x00602720</c>): splice
    /// the whole list onto a local sentinel, then repeatedly pop the front of that temporary list,
    /// re-link it onto the tail of the <em>real</em> (now-empty) list, and only then invoke
    /// <paramref name="dispatch"/> on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is why the pass is safe against mutation:
    /// </para>
    /// <list type="bullet">
    /// <item>An object is already unlinked from the temp list and re-linked into the real list
    /// <em>before</em> it is dispatched, so if <paramref name="dispatch"/> calls
    /// <see cref="Remove"/> on it (self-removal, the documented case), that unlink acts on the
    /// real list and cannot corrupt the temp list's traversal — <c>Unlink</c> only ever touches
    /// the two neighbours of the node being removed, regardless of which list it currently sits
    /// in.</item>
    /// <item>The same reasoning covers one dispatched object removing a <em>different</em>,
    /// not-yet-processed object: that object's node is still reachable from the temp sentinel
    /// through its own <c>Prev</c>/<c>Next</c>, so unlinking it there is equally safe.</item>
    /// <item>An object added via <see cref="Add"/> during the pass goes straight onto the real
    /// list (which the temp sentinel never points into), so it is not visited until the next
    /// pass — never processed twice, never processed while still being constructed by its own
    /// registration call.</item>
    /// </list>
    /// <para>
    /// <b>Scope of the evidence:</b> the doc's link-shuffle description is specific to
    /// <c>advanceObjects</c>, i.e. the <see cref="IProcessTickable.ProcessTick"/> dispatch. It
    /// says nothing about whether the client's two extra full passes
    /// (<see cref="IProcessTickable.InterpolateTick"/>/<see cref="IProcessTickable.AdvanceTime"/>)
    /// use the same mechanism or a plain walk. <see cref="AdvanceClientTime"/> therefore only
    /// uses this shuffle for the tick pass, and takes a simple list snapshot
    /// (<see cref="Objects"/>) for the other two — see its remarks.
    /// </para>
    /// </remarks>
    private void AdvanceObjects(Action<IProcessTickable> dispatch)
    {
        if (_head.Next == _head)
            return;

        var temp = new ProcessNode();
        temp.Next = _head.Next;
        temp.Prev = _head.Prev;
        temp.Next.Prev = temp;
        temp.Prev.Next = temp;
        _head.Next = _head;
        _head.Prev = _head;

        while (temp.Next != temp)
        {
            var node = temp.Next;
            Unlink(node);
            LinkBefore(_head, node);
            dispatch(node.Owner!);
        }
    }

    /// <summary>
    /// Runs the per-object dispatch for one 32ms tick. Gated on <see cref="IProcessTickable.ProcessTickEnabled"/>;
    /// a control object (non-null <see cref="IProcessTickable.ControllingConnection"/>) receives
    /// one <see cref="IProcessTickable.ProcessTick"/> per queued move, everyone else receives
    /// exactly one call with a null move. See <c>Sim/EngineTickLoop.md</c>,
    /// "<c>ProcessList::advanceObjects</c>".
    /// </summary>
    private static void DispatchProcessTick(IProcessTickable obj)
    {
        if (!obj.ProcessTickEnabled)
            return;

        if (obj.ControllingConnection is { } connection)
        {
            var moves = connection.FetchMoves();
            for (var i = 0; i < moves.Count; i++)
                obj.ProcessTick(moves[i]);
            connection.ClearMoves(moves.Count);
        }
        else
        {
            obj.ProcessTick(null);
        }
    }

    /// <summary>
    /// The fixed-step tick loop shared by server and client advance: accumulate
    /// <paramref name="elapsedMs"/> into <see cref="LastTime"/>, then run one
    /// <see cref="AdvanceObjects"/> processTick pass per whole 32ms boundary crossed. Returns the
    /// number of ticks run.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>target = (mLastTime + delta) &amp; ~0x1F</c> /
    /// <c>mLastTick += 0x20</c> per iteration exactly, including <see cref="LastTime"/> and
    /// <see cref="LastTick"/> both being running (never-reset) counters: the "remainder" carried
    /// into the next call falls out naturally as <c>LastTime - LastTick</c>, which is exactly
    /// what <c>mLastTime &amp; 31</c> reads in <see cref="AdvanceClientTime"/>'s interpolate-delta
    /// formula, because <see cref="LastTick"/> is always kept aligned to a 32ms boundary.
    /// </remarks>
    private int RunTickLoop(uint elapsedMs)
    {
        LastDelta = elapsedMs;

        var newTotal = LastTime + elapsedMs;
        var target = newTotal & ~(TickMilliseconds - 1);

        var ticks = 0;
        while (LastTick < target)
        {
            LastTick += TickMilliseconds;
            ticks++;
            AdvanceObjects(DispatchProcessTick);
        }

        LastTime = newTotal;
        return ticks;
    }

    private static uint ClampElapsed(uint elapsedMs) => Math.Min(elapsedMs, ElapsedClampMilliseconds);

    /// <summary>
    /// Server-side advance — <c>ProcessList::advanceServerTime</c> (<c>0x00602350</c>). Runs only
    /// the fixed-step <see cref="IProcessTickable.ProcessTick"/> pass, zero or more times
    /// depending on how many 32ms boundaries <paramref name="elapsedMs"/> crosses. Returns the
    /// number of ticks run.
    /// </summary>
    public int AdvanceServerTime(uint elapsedMs) => RunTickLoop(ClampElapsed(elapsedMs));

    /// <summary>
    /// Client-side advance — <c>ProcessList::advanceClientTime</c> (<c>0x00602430</c>). Runs the
    /// same fixed-step tick pass as <see cref="AdvanceServerTime"/>, then two further full passes
    /// over the list: <see cref="IProcessTickable.InterpolateTick"/> with the fractional
    /// remainder <c>(32 - (mLastTime &amp; 31)) / 32</c>, then
    /// <see cref="IProcessTickable.AdvanceTime"/> with <see cref="LastDelta"/> converted from
    /// milliseconds to seconds. These two passes are plain forward walks over a snapshot
    /// (<see cref="Objects"/>), not the link-shuffle used for the tick pass — see the scope note
    /// on <see cref="AdvanceObjects"/>. Returns the number of ticks run.
    /// </summary>
    public int AdvanceClientTime(uint elapsedMs)
    {
        var ticks = RunTickLoop(ClampElapsed(elapsedMs));

        var interpolateDelta = (TickMilliseconds - (LastTime & (TickMilliseconds - 1))) * (1f / TickMilliseconds);
        foreach (var obj in Objects)
            obj.InterpolateTick(interpolateDelta);

        var advanceDeltaSeconds = LastDelta * 0.001f;
        foreach (var obj in Objects)
        {
            if (obj.AdvanceTimeEnabled)
                obj.AdvanceTime(advanceDeltaSeconds);
        }

        return ticks;
    }
}
