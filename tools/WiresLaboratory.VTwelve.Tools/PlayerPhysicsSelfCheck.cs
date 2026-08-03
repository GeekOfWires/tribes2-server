using WiresLaboratory.VTwelve.Sim.Datablocks;
using WiresLaboratory.VTwelve.Sim.Physics;

namespace WiresLaboratory.VTwelve.Tools;

/// <summary>
/// Deterministic self-check for <c>Sim/Physics</c> — the managed re-implementation of the player
/// movement model documented in <c>src/WiresLaboratory.VTwelve/Sim/PlayerPhysics.md</c>
/// (<c>Player::updateMove</c> / <c>Player::updatePos</c>).
/// </summary>
/// <remarks>
/// Dispatched from <see cref="Program"/> via <c>--physicscheck</c>, the same pattern
/// <c>--tickcheck</c>/<c>--pcap</c> use. Mirrors <see cref="TickLoopSelfCheck"/>'s structure
/// (a list of named <see cref="Check"/> calls, <see cref="Assert"/>/<see cref="AssertClose"/>
/// helpers) deliberately, for consistency across the self-checks in this project.
/// <para>
/// The tuning values used throughout (<see cref="NewPlayerData"/>) are clean, synthetic round
/// numbers chosen to make the arithmetic easy to hand-verify — they are NOT the shipped
/// <c>PlayerData</c> defaults, which this project has not loaded from any real game data here.
/// </para>
/// </remarks>
public static class PlayerPhysicsSelfCheck
{
    public static int Run(string[] args)
    {
        var failures = 0;

        failures += Check("free-fall over 10 ticks matches the closed-form semi-implicit-Euler result", CheckFreeFall);
        failures += Check("jump is an impulse (jumpForce / mass), with NO dt factor", CheckJumpImpulse);
        failures += Check("horizontal resist bounds speed under sustained forward force", CheckHorizontalResistBoundsSpeed);
        failures += Check("ground collision: v += n * (-(v.n) + 0.01f), no restitution", CheckGroundCollisionResponse);
        failures += Check("5-retry exhaustion rolls the entire tick back", CheckRetryExhaustionRollsBack);
        failures += Check("ground-displacement quirk: pure-Z displacement is silently dropped (X/Y-only zero-test)", CheckGroundDisplacementZQuirk);

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "RESULT: player physics self-check clean — all assertions passed."
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

    private static void AssertClose(float expected, float actual, string message, float tolerance = 1e-4f)
    {
        if (MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    // ------------------------------------------------------------------------------------------
    // 1. Free-fall matches the closed-form semi-implicit-Euler result.
    // ------------------------------------------------------------------------------------------
    private static void CheckFreeFall()
    {
        const float gravity = -20.0f;
        const float dt = PhysicsWorld.TickSeconds; // 0.03125f -- see the const's remarks: NOT 0.032f.
        const int ticks = 10;

        var tuning = PlayerTuning.FromDatablock(NewPlayerData());
        var state = PlayerPhysicsState.AtRest(new PhysVector3(0f, 0f, 1000f), tuning); // high up: never contacts the ground below.
        var farGround = new AnalyticGroundPlane(groundZ: -1_000_000f);
        var box = new PlayerCollisionBox(HalfWidth: 0.4f, MinZ: 0f, MaxZ: 1.8f);

        for (var i = 0; i < ticks; i++)
        {
            PlayerForcePhase.ApplyMove(state, PlayerMoveInput.None, tuning, gravity, dt);
            var ok = PlayerPositionPhase.Integrate(state, farGround, box, tuning, dt);
            Assert(ok, $"tick {i}: free-fall integration should never hit the retry cap");
        }

        // Semi-implicit Euler, split across two phases (PlayerPhysics.md, "Integration scheme"):
        // updateMove finishes velocity FIRST, updatePos integrates position from the
        // already-updated velocity. Closed form for N steps of v_k = v_(k-1) + g*dt,
        // y_N = y_0 + dt * sum_{k=1..N} v_k = y_0 + g*dt^2 * N*(N+1)/2.
        var expectedVelocityZ = gravity * dt * ticks;
        var expectedPositionZ = 1000f + gravity * dt * dt * (ticks * (ticks + 1) / 2f);

        AssertClose(expectedVelocityZ, state.Velocity.Z, "velocity.Z after 10 ticks of free-fall");
        AssertClose(expectedPositionZ, state.Position.Z, "position.Z after 10 ticks of free-fall", tolerance: 1e-3f);

        Console.WriteLine($"        10 ticks @ g={gravity}, dt=1/32: velocity.Z={state.Velocity.Z:0.######} (expected {expectedVelocityZ:0.######}), " +
                           $"position.Z={state.Position.Z:0.######} (expected {expectedPositionZ:0.######})");
    }

    // ------------------------------------------------------------------------------------------
    // 2. Jump: impulse jumpForce / mass, no dt factor -- the one documented exception.
    // ------------------------------------------------------------------------------------------
    private static void CheckJumpImpulse()
    {
        var data = NewPlayerData();
        data.Set("jumpForce", 180f);
        data.Set("mass", 9f);
        var tuning = PlayerTuning.FromDatablock(data);

        var state = PlayerPhysicsState.AtRest(PhysVector3.Zero, tuning);
        state.OnGround = true;
        state.ContactNormal = PhysVector3.UnitZ;
        state.Energy = tuning.MaxEnergy;

        // gravity = 0 for this call only, to isolate the jump impulse from the (also real, but
        // separately tested) gravity term -- a normal tick applies both in the same call.
        PlayerForcePhase.ApplyMove(state, new PlayerMoveInput(PhysVector3.Zero, Jump: true, Jet: false), tuning, gravity: 0f);

        var expected = tuning.JumpForce / tuning.Mass; // 180 / 9 = 20.0 exactly
        var dtScaledWrongAnswer = expected * PhysicsWorld.TickSeconds; // what a (wrong) dt-scaled impulse would give: 0.625

        AssertClose(expected, state.Velocity.Z, "jump impulse velocity.Z (jumpForce / mass, no dt)");
        Assert(MathF.Abs(state.Velocity.Z - dtScaledWrongAnswer) > 1f,
            "the result must NOT match a dt-scaled impulse -- that would mean dt leaked into the jump term");

        Console.WriteLine($"        jumpForce=180, mass=9 -> velocity.Z={state.Velocity.Z} (expected {expected}; a dt-scaled impulse would wrongly give {dtScaledWrongAnswer})");
    }

    // ------------------------------------------------------------------------------------------
    // 3. Horizontal resist bounds speed under sustained forward force.
    // ------------------------------------------------------------------------------------------
    private static void CheckHorizontalResistBoundsSpeed()
    {
        var tuning = PlayerTuning.FromDatablock(NewPlayerData()); // horizResistSpeed=4, horizMaxSpeed=10, horizResistFactor=0.3, runForce=90, mass=9.

        var state = PlayerPhysicsState.AtRest(PhysVector3.Zero, tuning);
        state.OnGround = true;
        state.Energy = tuning.MaxEnergy;
        var forward = new PlayerMoveInput(new PhysVector3(1f, 0f, 0f), Jump: false, Jet: false);

        float speedAt100 = 0f, speedAt300 = 0f;
        for (var i = 1; i <= 300; i++)
        {
            state.Energy = tuning.MaxEnergy; // runEnergyDrain=0 in the baseline tuning; kept explicit so this test doesn't depend on that.
            PlayerForcePhase.ApplyMove(state, forward, tuning, gravity: 0f); // gravity=0: isolate horizontal behaviour.
            if (i == 100) speedAt100 = state.Velocity.Horizontal.Length;
            if (i == 300) speedAt300 = state.Velocity.Horizontal.Length;
        }

        Assert(speedAt100 > tuning.HorizResistSpeed, $"expected sustained force to push speed past horizResistSpeed ({tuning.HorizResistSpeed}) by tick 100, got {speedAt100}");
        Assert(speedAt300 <= tuning.HorizMaxSpeed + 1e-3f, $"expected speed to stay bounded at horizMaxSpeed ({tuning.HorizMaxSpeed}), got {speedAt300}");
        Assert(MathF.Abs(speedAt300 - speedAt100) < 0.5f, "expected speed to have converged (not still climbing) between tick 100 and tick 300");

        Console.WriteLine($"        horizResistSpeed={tuning.HorizResistSpeed}, horizMaxSpeed={tuning.HorizMaxSpeed}: speed@100={speedAt100:0.####}, speed@300={speedAt300:0.####}");
    }

    // ------------------------------------------------------------------------------------------
    // 4. Ground collision: v += n * (-(v.n) + 0.01f) -- no restitution.
    // ------------------------------------------------------------------------------------------
    private static void CheckGroundCollisionResponse()
    {
        var tuning = PlayerTuning.FromDatablock(NewPlayerData()); // runSurfaceAngle=60 deg -> cos = 0.5
        var ground = new AnalyticGroundPlane(groundZ: 0f);
        var box = new PlayerCollisionBox(HalfWidth: 0.4f, MinZ: 0f, MaxZ: 1.8f);
        const float dt = PhysicsWorld.TickSeconds;

        // Two very different impact speeds. A restitution model would produce two very
        // different post-impact speeds (proportional to the impact speed); the documented
        // formula does not -- it always leaves the same tiny separation velocity regardless of
        // how hard the impact was.
        foreach (var impactSpeed in new[] { 5f, 50f })
        {
            var state = PlayerPhysicsState.AtRest(new PhysVector3(0f, 0f, impactSpeed * dt), tuning);
            state.Velocity = new PhysVector3(0f, 0f, -impactSpeed);

            var ok = PlayerPositionPhase.Integrate(state, ground, box, tuning, dt);

            Assert(ok, $"impact speed {impactSpeed}: single contact exactly consuming the tick should not hit the retry cap");
            AssertClose(0.01f, state.Velocity.Z, $"impact speed {impactSpeed}: post-collision velocity.Z (separation bias only, no restitution)", tolerance: 1e-4f);
            Assert(state.OnGround, $"impact speed {impactSpeed}: a normal.Z=1 contact (cos=0.5 threshold) should register as ground contact");

            Console.WriteLine($"        impact speed {impactSpeed} -> post-collision velocity.Z={state.Velocity.Z} (expected 0.01 regardless of impact speed)");
        }
    }

    // ------------------------------------------------------------------------------------------
    // 5. 5-retry exhaustion rolls the entire tick back.
    // ------------------------------------------------------------------------------------------
    private static void CheckRetryExhaustionRollsBack()
    {
        var tuning = PlayerTuning.FromDatablock(NewPlayerData());
        var startPosition = new PhysVector3(1f, 2f, 3f);
        var startVelocity = new PhysVector3(5f, 0f, 0f);

        var state = PlayerPhysicsState.AtRest(startPosition, tuning);
        state.Velocity = startVelocity;

        // A pathological surface that reports contact at t=0 on every single sweep -- every
        // retry consumes zero time (consumedTime = moveTime * 0), so `remainingTime` can never
        // reach zero and the loop is guaranteed to hit MaxRetries (5).
        var wall = new AlwaysImmediateContactSurface(new PhysVector3(1f, 0f, 0f));
        var box = new PlayerCollisionBox(HalfWidth: 0.4f, MinZ: 0f, MaxZ: 1.8f);

        var ok = PlayerPositionPhase.Integrate(state, wall, box, tuning, PhysicsWorld.TickSeconds);

        Assert(!ok, "expected Integrate to report failure (retry cap exhausted)");
        Assert(state.Position == startPosition, $"expected position rolled back to {startPosition}, got {state.Position}");
        Assert(state.Velocity == PhysVector3.Zero, $"expected velocity zeroed on rollback, got {state.Velocity}");

        Console.WriteLine($"        pathological always-contact surface -> rolled back: position={state.Position} (== start), velocity={state.Velocity} (zeroed)");
    }

    /// <summary>Test double: reports an immediate (t=0) contact against a fixed normal on every sweep, regardless of motion.</summary>
    private sealed class AlwaysImmediateContactSurface(PhysVector3 normal) : ICollisionSurface
    {
        public bool TryFindContact(PlayerCollisionBox box, PhysVector3 start, PhysVector3 motion, out float contactTime, out PhysVector3 outNormal, out float maxHeight)
        {
            contactTime = 0f;
            outNormal = normal;
            maxHeight = start.Z + box.MinZ;
            return true;
        }
    }

    // ------------------------------------------------------------------------------------------
    // 6. Ground-displacement quirk: the zero-test checks only .X and .Y (PlayerPhysics.md, "Gaps").
    // ------------------------------------------------------------------------------------------
    private static void CheckGroundDisplacementZQuirk()
    {
        var tuning = PlayerTuning.FromDatablock(NewPlayerData());
        var farGround = new AnalyticGroundPlane(groundZ: -1_000_000f); // never contacted -- isolates the displacement step itself.
        var box = new PlayerCollisionBox(HalfWidth: 0.4f, MinZ: 0f, MaxZ: 1.8f);
        var start = new PhysVector3(0f, 0f, 100f);

        // Pure-Z displacement (e.g. a platform moving straight up, no horizontal motion):
        // silently dropped. This is the confirmed quirk, not a bug -- see PlayerPositionPhase.
        var zOnly = PlayerPhysicsState.AtRest(start, tuning);
        zOnly.Velocity = PhysVector3.Zero;
        PlayerPositionPhase.Integrate(zOnly, farGround, box, tuning, PhysicsWorld.TickSeconds, groundDisplacement: new PhysVector3(0f, 0f, 5f));
        Assert(MathF.Abs(zOnly.Position.Z - start.Z) < 1e-6f,
            $"pure-Z ground displacement must be SILENTLY DROPPED (confirmed engine quirk); position.Z moved from {start.Z} to {zOnly.Position.Z}");

        // Any non-zero X or Y makes the zero-test pass, and Z rides along with it -- the same
        // displacement vector is NOT dropped once X or Y is non-zero.
        var withXy = PlayerPhysicsState.AtRest(start, tuning);
        withXy.Velocity = PhysVector3.Zero;
        PlayerPositionPhase.Integrate(withXy, farGround, box, tuning, PhysicsWorld.TickSeconds, groundDisplacement: new PhysVector3(1f, 0f, 5f));
        AssertClose(start.Z + 5f, withXy.Position.Z, "with a non-zero X component, the same Z displacement must be applied");
        AssertClose(start.X + 1f, withXy.Position.X, "and X itself must be applied");

        Console.WriteLine($"        Z-only displacement (0,0,5): position.Z unchanged at {zOnly.Position.Z} (dropped, as the client does)");
        Console.WriteLine($"        X+Z displacement (1,0,5): position=({withXy.Position.X},{withXy.Position.Y},{withXy.Position.Z}) (both components applied)");
    }

    // ------------------------------------------------------------------------------------------
    // Shared tuning fixture.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a <c>PlayerData</c> instance with clean, synthetic round-number tuning -- NOT the
    /// shipped defaults (this project has no loaded/parsed real game datablock instance to read
    /// them from here). Individual checks that need a different value for one field clone this
    /// via <see cref="DatablockInstance.Set{T}"/> after construction.
    /// </summary>
    private static DatablockInstance NewPlayerData()
    {
        var registry = DatablockFieldRegistry.LoadEmbedded();
        var d = new DatablockInstance(registry, "PlayerData");

        d.Set("mass", 9f);
        d.Set("drag", 0f);
        d.Set("density", 1.1f);
        d.Set("maxEnergy", 100f);

        d.Set("maxForwardSpeed", 10f);
        d.Set("maxBackwardSpeed", 8f);
        d.Set("maxSideSpeed", 8f);

        d.Set("runForce", 90f);
        d.Set("runEnergyDrain", 0f);
        d.Set("minRunEnergy", 1f);

        d.Set("maxStepHeight", 0.5f);
        d.Set("runSurfaceAngle", 60f); // degrees; PlayerTuning converts this to a cosine.

        d.Set("horizMaxSpeed", 10f);
        d.Set("horizResistSpeed", 4f);
        d.Set("horizResistFactor", 0.3f);

        d.Set("upMaxSpeed", 1000f);   // effectively "off" for tests that don't specifically exercise vertical resist.
        d.Set("upResistSpeed", 1000f);
        d.Set("upResistFactor", 0.3f);

        d.Set("jumpForce", 180f);
        d.Set("jumpEnergyDrain", 8f);
        d.Set("minJumpEnergy", 8f);
        d.Set("minJumpSpeed", 0f);
        d.Set("maxJumpSpeed", 1000f);
        d.Set("jumpSurfaceAngle", 60f); // degrees; PlayerTuning converts this to a cosine.
        d.Set("jumpDelay", 0);

        d.Set("jetForce", 40f);
        d.Set("underwaterJetForce", 20f);
        d.Set("jetEnergyDrain", 1f);
        d.Set("minJetEnergy", 5f);
        d.Set("maxJetForwardSpeed", 10f);
        d.Set("maxJetHorizontalPercentage", 0.5f);

        return d;
    }
}
