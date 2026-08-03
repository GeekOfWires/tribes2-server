using WiresLaboratory.VTwelve.Sim.Process;

namespace WiresLaboratory.VTwelve.Tools;

/// <summary>
/// Deterministic self-check for <see cref="ProcessList"/> — the managed re-implementation of
/// the recovered <c>ProcessList::advanceServerTime</c>/<c>advanceClientTime</c> tick loop
/// documented in <c>src/WiresLaboratory.VTwelve/Sim/EngineTickLoop.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a separate entry point, not a dispatch branch added to <see cref="Program"/> (that
/// file is owned by other work in progress). It has its own <c>Main</c>; the project's
/// <c>&lt;StartupObject&gt;</c> is pinned to <see cref="Program"/> so the default build/run
/// behaviour of the Tools project is unchanged, and this check is run by overriding that
/// property on the command line:
/// </para>
/// <code>
/// dotnet run --project tools/WiresLaboratory.VTwelve.Tools -c Release -p:StartupObject=WiresLaboratory.VTwelve.Tools.TickLoopSelfCheck
/// </code>
/// <para>
/// or, against an already-built output:
/// </para>
/// <code>
/// dotnet exec tools/WiresLaboratory.VTwelve.Tools/bin/Release/net10.0/WiresLaboratory.VTwelve.Tools.dll --tick-self-check
/// </code>
/// <para>
/// (the <c>--tick-self-check</c> argument is accepted and ignored by this entry point — it is
/// there only so the same command line reads sensibly either way; <c>Main</c> here runs the
/// checks unconditionally since it is never reached except via the <c>StartupObject</c> override
/// above.)
/// </para>
/// </remarks>
public static class TickLoopSelfCheck
{
    public static int Run(string[] args)
    {
        var failures = 0;

        failures += Check("100ms server advance -> 3 ticks, 4ms remainder", CheckFixedTimestepAndRemainder);
        failures += Check("interpolateTick fractional deltas at several sub-tick offsets", CheckInterpolateFraction);
        failures += Check("advanceTime receives seconds, not milliseconds", CheckAdvanceTimeIsSeconds);
        failures += Check("self-removal during a pass does not corrupt iteration", CheckSelfRemovalDuringPass);
        failures += Check("1024ms elapsed clamp bounds tick catch-up", CheckElapsedClamp);
        failures += Check("control-object move-list dispatch (fetch/process-per-move/clear)", CheckControlObjectMoveDispatch);

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "RESULT: tick loop self-check clean — all assertions passed."
            : $"RESULT: {failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(string name, Action check)
    {
        try
        {
            check();
            Console.WriteLine($"  PASS  {name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertClose(float expected, float actual, string message, float tolerance = 1e-6f)
    {
        if (MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    // ------------------------------------------------------------------------------------------
    // 1. Fixed timestep + remainder.
    // ------------------------------------------------------------------------------------------
    private static void CheckFixedTimestepAndRemainder()
    {
        var list = new ProcessList();
        var recorder = new RecordingTickable();
        list.Add(recorder);

        var ticks = list.AdvanceServerTime(100);

        Assert(ticks == 3, $"expected 3 ticks for a 100ms advance, got {ticks}");
        Assert(list.LastTick == 96, $"expected LastTick == 96 (3 * 32), got {list.LastTick}");
        Assert(list.LastTime == 100, $"expected LastTime == 100 (running total, not tick-aligned), got {list.LastTime}");
        Assert(list.LastTime - list.LastTick == 4, $"expected a 4ms remainder, got {list.LastTime - list.LastTick}");
        Assert(recorder.ProcessTickCalls == 3, $"expected 3 ProcessTick dispatches (one per tick), got {recorder.ProcessTickCalls}");

        Console.WriteLine($"        100ms -> {ticks} ticks, LastTick={list.LastTick}, LastTime={list.LastTime}, remainder={list.LastTime - list.LastTick}ms");
    }

    // ------------------------------------------------------------------------------------------
    // 2. interpolateTick fractional remainder, at several sub-tick offsets.
    // ------------------------------------------------------------------------------------------
    private static void CheckInterpolateFraction()
    {
        // (elapsedMs for a single AdvanceClientTime call from a fresh list, expected remainder = elapsedMs & 31)
        var cases = new (uint ElapsedMs, uint ExpectedRemainder, float ExpectedDelta)[]
        {
            (0, 0, 1.0f),        // exactly on a tick boundary: (32 - 0) / 32
            (1, 1, 31f / 32f),
            (16, 16, 16f / 32f), // halfway through a tick
            (31, 31, 1f / 32f),  // one ms short of the next boundary
            (50, 18, 14f / 32f), // spans one full tick (50 = 32 + 18)
        };

        foreach (var (elapsedMs, expectedRemainder, expectedDelta) in cases)
        {
            var list = new ProcessList();
            var recorder = new RecordingTickable();
            list.Add(recorder);

            list.AdvanceClientTime(elapsedMs);

            Assert((list.LastTime & 31) == expectedRemainder,
                $"elapsed={elapsedMs}: expected LastTime&31 == {expectedRemainder}, got {list.LastTime & 31}");
            Assert(recorder.InterpolateDeltas.Count == 1,
                $"elapsed={elapsedMs}: expected exactly 1 InterpolateTick call, got {recorder.InterpolateDeltas.Count}");
            AssertClose(expectedDelta, recorder.InterpolateDeltas[0],
                $"elapsed={elapsedMs}: interpolateTick delta");

            Console.WriteLine($"        elapsed={elapsedMs,3}ms -> remainder={list.LastTime & 31,2}ms, interpolateTick(delta={recorder.InterpolateDeltas[0]:0.#####})");
        }
    }

    // ------------------------------------------------------------------------------------------
    // 3. advanceTime receives seconds, not milliseconds.
    // ------------------------------------------------------------------------------------------
    private static void CheckAdvanceTimeIsSeconds()
    {
        var list = new ProcessList();
        var recorder = new RecordingTickable();
        list.Add(recorder);

        list.AdvanceClientTime(100);

        Assert(recorder.AdvanceSeconds.Count == 1, $"expected exactly 1 AdvanceTime call, got {recorder.AdvanceSeconds.Count}");
        AssertClose(0.1f, recorder.AdvanceSeconds[0], "advanceTime(delta) for a 100ms advance");
        Assert(recorder.AdvanceSeconds[0] < 1f, "advanceTime must not have received raw milliseconds (100), it must be seconds (0.1)");

        Console.WriteLine($"        100ms advance -> advanceTime(deltaSeconds={recorder.AdvanceSeconds[0]})");
    }

    // ------------------------------------------------------------------------------------------
    // 4. Self-removal during a pass does not corrupt iteration.
    // ------------------------------------------------------------------------------------------
    private static void CheckSelfRemovalDuringPass()
    {
        var list = new ProcessList();
        var b = new RecordingTickable();
        var c = new RecordingTickable();
        SelfRemovingTickable? a = null;
        a = new SelfRemovingTickable(() => list.Remove(a!));

        list.Add(a);
        list.Add(b);
        list.Add(c);
        Assert(list.Count == 3, "expected 3 objects registered before the first pass");

        // One 32ms tick: exactly one processTick pass.
        var ticks = list.AdvanceServerTime(32);

        Assert(ticks == 1, $"expected exactly 1 tick, got {ticks}");
        Assert(a.ProcessTickCalls == 1, $"self-removing object should still have been ticked once, got {a.ProcessTickCalls}");
        Assert(b.ProcessTickCalls == 1, $"expected b ticked once, got {b.ProcessTickCalls}");
        Assert(c.ProcessTickCalls == 1, $"expected c ticked once, got {c.ProcessTickCalls}");
        Assert(list.Count == 2, $"expected a to have removed itself, leaving 2 objects, got {list.Count}");
        Assert(!list.Objects.Contains(a), "a should no longer be registered after removing itself");
        Assert(list.Objects.Contains(b) && list.Objects.Contains(c), "b and c should remain registered");

        // A second pass must not re-touch a, and must still cleanly tick the survivors once each.
        list.AdvanceServerTime(32);
        Assert(a.ProcessTickCalls == 1, "a must not be dispatched again after removing itself");
        Assert(b.ProcessTickCalls == 2 && c.ProcessTickCalls == 2, "b and c must each be ticked exactly once per subsequent pass");

        Console.WriteLine($"        3 objects registered, a removes itself mid-pass -> a={a.ProcessTickCalls} b={b.ProcessTickCalls} c={c.ProcessTickCalls} tick(s), {list.Count} remaining");
    }

    // ------------------------------------------------------------------------------------------
    // 5. The 1024ms elapsed-time clamp bounds catch-up.
    // ------------------------------------------------------------------------------------------
    private static void CheckElapsedClamp()
    {
        var list = new ProcessList();
        var recorder = new RecordingTickable();
        list.Add(recorder);

        // Simulate a multi-second stall: without the clamp this would be ~156 ticks.
        var ticks = list.AdvanceServerTime(5000);

        var expectedTicks = (int)(ProcessList.ElapsedClampMilliseconds / ProcessList.StockTickMilliseconds);
        Assert(ticks == expectedTicks, $"expected the clamp to bound this to {expectedTicks} ticks, got {ticks}");
        Assert(list.LastTime == ProcessList.ElapsedClampMilliseconds,
            $"expected LastTime clamped to {ProcessList.ElapsedClampMilliseconds}, got {list.LastTime}");
        Assert(list.LastTime != 5000, "elapsed time must have been clamped, not applied as-is");

        Console.WriteLine($"        5000ms stall clamped to {ProcessList.ElapsedClampMilliseconds}ms -> {ticks} ticks (unclamped would be {5000 / ProcessList.StockTickMilliseconds})");
    }

    // ------------------------------------------------------------------------------------------
    // 6. Bonus: control-object move-list dispatch.
    // ------------------------------------------------------------------------------------------
    private static void CheckControlObjectMoveDispatch()
    {
        var list = new ProcessList();
        var moves = new TestMoveSource(new Move(), new Move(), new Move());
        var control = new RecordingTickable { ControllingConnection = moves };
        var plain = new RecordingTickable();

        list.Add(control);
        list.Add(plain);

        list.AdvanceServerTime(32);

        Assert(control.ProcessTickCalls == 3, $"expected one ProcessTick per queued move (3), got {control.ProcessTickCalls}");
        Assert(control.ReceivedNullMove == 0, "control object must never receive a null move while moves are queued");
        Assert(moves.ClearedCount == 3, $"expected ClearMoves(3) after dispatch, got ClearMoves({moves.ClearedCount})");
        Assert(plain.ProcessTickCalls == 1, $"expected a plain object to get exactly one ProcessTick(null), got {plain.ProcessTickCalls}");
        Assert(plain.ReceivedNullMove == 1, "a plain object's single ProcessTick call must carry a null move");

        Console.WriteLine($"        control object: {control.ProcessTickCalls} moves dispatched, ClearMoves({moves.ClearedCount}); plain object: {plain.ProcessTickCalls} call(s), null move");
    }

    // ------------------------------------------------------------------------------------------
    // Test doubles.
    // ------------------------------------------------------------------------------------------

    private sealed class RecordingTickable : IProcessTickable
    {
        public bool ProcessTickEnabled { get; set; } = true;
        public bool AdvanceTimeEnabled { get; set; } = true;
        public IMoveSource? ControllingConnection { get; set; }

        public int ProcessTickCalls { get; private set; }
        public int ReceivedNullMove { get; private set; }
        public List<float> InterpolateDeltas { get; } = new();
        public List<float> AdvanceSeconds { get; } = new();

        public void ProcessTick(Move? move)
        {
            ProcessTickCalls++;
            if (move is null) ReceivedNullMove++;
        }

        public void InterpolateTick(float delta) => InterpolateDeltas.Add(delta);

        public void AdvanceTime(float deltaSeconds) => AdvanceSeconds.Add(deltaSeconds);
    }

    /// <summary>A tickable that removes itself from its owning list the first time it is ticked.</summary>
    private sealed class SelfRemovingTickable(Action removeSelf) : IProcessTickable
    {
        public bool ProcessTickEnabled => true;
        public bool AdvanceTimeEnabled => true;
        public IMoveSource? ControllingConnection => null;

        public int ProcessTickCalls { get; private set; }

        public void ProcessTick(Move? move)
        {
            ProcessTickCalls++;
            removeSelf();
        }

        public void InterpolateTick(float delta) { }
        public void AdvanceTime(float deltaSeconds) { }
    }

    private sealed class TestMoveSource(params Move[] queued) : IMoveSource
    {
        private List<Move> _queue = new(queued);

        public int ClearedCount { get; private set; }

        public IReadOnlyList<Move> FetchMoves() => _queue;

        public void ClearMoves(int count)
        {
            ClearedCount = count;
            _queue = _queue.Count > count ? _queue.GetRange(count, _queue.Count - count) : new List<Move>();
        }
    }
}
