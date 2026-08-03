namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// The force phase — the managed analogue of <c>Player::updateMove</c>
/// (<c>0x005d2d60</c>/<c>0x1e50</c>, per <c>PlayerPhysics.md</c>). Turns one tick's input into a
/// finished <see cref="PlayerPhysicsState.Velocity"/>. Position/collision is the separate
/// <see cref="PlayerPositionPhase"/> — the engine recovers these as two distinct functions and
/// this port mirrors that split rather than merging them for convenience.
/// </summary>
public static class PlayerForcePhase
{
    /// <summary>
    /// Applies one tick of input, in the documented order:
    /// <c>v += acc</c>, then horizontal resist, vertical resist, buoyancy, drag
    /// (<c>PlayerPhysics.md</c>, "Order of operations after the velocity update").
    /// </summary>
    /// <param name="state">Mutated in place: <see cref="PlayerPhysicsState.Velocity"/>, <see cref="PlayerPhysicsState.Energy"/>, <see cref="PlayerPhysicsState.JumpCooldownTicks"/>.</param>
    /// <param name="input">This tick's resolved movement input — see <see cref="PlayerMoveInput"/>.</param>
    /// <param name="tuning">The player's datablock tunables.</param>
    /// <param name="gravity">World gravity for this tick — <see cref="PhysicsWorld.Gravity"/>, read fresh rather than assumed constant.</param>
    /// <param name="dt">Tick length in seconds. Defaults to <see cref="PhysicsWorld.TickSeconds"/> (1/32 s exactly).</param>
    public static void ApplyMove(
        PlayerPhysicsState state,
        PlayerMoveInput input,
        PlayerTuning tuning,
        float gravity,
        float dt = PhysicsWorld.TickSeconds)
    {
        var acc = PhysVector3.Zero;

        // Gravity is an ACCELERATION, not a force: no /mass. It still needs the dt pre-scale,
        // because the integration site below (`velocity += acc`) applies neither dt nor 1/mass
        // itself — every contributing term must arrive pre-scaled (PlayerPhysics.md,
        // "Integration scheme").
        acc += new PhysVector3(0f, 0f, gravity * dt);

        // Run force: a datablock FORCE (runForce), so unlike gravity it needs /mass to become an
        // acceleration before the same dt pre-scale. Gated on ground contact and available
        // energy, matching runEnergyDrain/minRunEnergy existing specifically as a run-state
        // gate/cost pair.
        if (state.OnGround && state.Energy > tuning.MinRunEnergy && input.MoveDirection != PhysVector3.Zero)
        {
            var direction = input.MoveDirection.Normalized();
            var runAccel = tuning.RunForce / tuning.Mass;
            acc += direction * (runAccel * dt);
            state.Energy = MathF.Max(0f, state.Energy - tuning.RunEnergyDrain * dt);
        }

        // Jump: the one documented EXCEPTION to "no dt, pre-scaled by the caller" — an impulse,
        // jumpForce / mass, with no TickSec factor at all (PlayerPhysics.md, "Integration
        // scheme"). Applied directly to velocity, not folded into `acc`, so it is visibly exempt
        // from the dt scaling every other term here gets.
        if (input.Jump && CanJump(state, tuning))
        {
            var vz = state.Velocity.Z + tuning.JumpForce / tuning.Mass;

            // ASSUMPTION, not disassembly-verified: PlayerPhysics.md documents the jump impulse
            // itself but says nothing about minJumpSpeed/maxJumpSpeed. Those two fields exist on
            // PlayerData (RecoveredDatablockFields.tsv) with names that only make sense as a
            // post-impulse clamp on vertical speed, so that is what is implemented here — but
            // it is inferred from field naming, not read out of the disassembly.
            vz = Math.Clamp(vz, tuning.MinJumpSpeed, tuning.MaxJumpSpeed);

            state.Velocity = state.Velocity with { Z = vz };
            state.Energy = MathF.Max(0f, state.Energy - tuning.JumpEnergyDrain);
            state.JumpCooldownTicks = tuning.JumpDelayTicks;
            state.OnGround = false;
        }

        // 1. v += acc — no mass division, no dt at this site (see remarks above).
        var velocity = state.Velocity + acc;

        // 2. horizontal resist, 3. vertical resist, 4. buoyancy, 5. drag — this exact order,
        // per PlayerPhysics.md. Water is not modelled by this port yet (no liquid-volume
        // detection exists), so buoyancy/drag are called — preserving the order and the call
        // site — but always with waterCoverage = 0, which per PlayerPhysics.md ("`mDrag` is
        // zero out of water") is a documented no-op for drag on land, and analogously inert for
        // buoyancy.
        velocity = ApplyHorizontalResist(velocity, tuning);
        velocity = ApplyVerticalResist(velocity, tuning);
        velocity = ApplyBuoyancy(velocity, tuning, gravity, dt, waterCoverage: 0f);
        velocity = ApplyDrag(velocity, tuning, waterCoverage: 0f);

        state.Velocity = velocity;

        if (state.JumpCooldownTicks > 0) state.JumpCooldownTicks--;
    }

    private static bool CanJump(PlayerPhysicsState state, PlayerTuning tuning) =>
        state.OnGround &&
        state.JumpCooldownTicks <= 0 &&
        state.Energy >= tuning.MinJumpEnergy &&
        state.ContactNormal.Z > tuning.JumpSurfaceAngleCosine; // cosine comparison — see PlayerTuning remarks.

    /// <summary>
    /// Bounds horizontal (X/Y) speed. Above <see cref="PlayerTuning.HorizMaxSpeed"/> this is a
    /// hard clamp; between <see cref="PlayerTuning.HorizResistSpeed"/> and that ceiling, the
    /// excess above <c>horizResistSpeed</c> decays geometrically at <c>horizResistFactor</c> per
    /// tick rather than being clamped instantly.
    /// </summary>
    /// <remarks>
    /// <b>Assumption:</b> <c>PlayerPhysics.md</c> establishes that this stage exists, runs
    /// second in the fixed post-update order, and (together with the vertical stage) is where
    /// ALL land speed limiting happens since drag is zero out of water — but it does not spell
    /// out the resist curve's shape. The two-tier hard-clamp/soft-decay split implemented here
    /// is the conventional shape for a Torque-lineage <c>*ResistSpeed</c>/<c>*ResistFactor</c>
    /// pair (chosen so both <see cref="PlayerTuning.HorizResistSpeed"/> and
    /// <see cref="PlayerTuning.HorizResistFactor"/> have an observable effect), not a formula
    /// recovered from the disassembly. Treat the bounding behaviour as verified, the exact curve
    /// as unverified.
    /// </remarks>
    internal static PhysVector3 ApplyHorizontalResist(PhysVector3 velocity, PlayerTuning tuning)
    {
        var horizontal = velocity.Horizontal;
        var speed = horizontal.Length;
        if (speed <= tuning.HorizResistSpeed || speed <= 1e-8f) return velocity;

        float scale;
        if (speed > tuning.HorizMaxSpeed)
        {
            scale = tuning.HorizMaxSpeed / speed;
        }
        else
        {
            var excess = speed - tuning.HorizResistSpeed;
            var decayedExcess = excess * (1f - tuning.HorizResistFactor);
            var newSpeed = tuning.HorizResistSpeed + decayedExcess;
            scale = newSpeed / speed;
        }

        return new PhysVector3(velocity.X * scale, velocity.Y * scale, velocity.Z);
    }

    /// <summary>Vertical analogue of <see cref="ApplyHorizontalResist"/>, operating on <c>|velocity.Z|</c> (both climbing and falling). Same assumption caveat applies.</summary>
    internal static PhysVector3 ApplyVerticalResist(PhysVector3 velocity, PlayerTuning tuning)
    {
        var speed = MathF.Abs(velocity.Z);
        if (speed <= tuning.UpResistSpeed || speed <= 1e-8f) return velocity;

        float scale;
        if (speed > tuning.UpMaxSpeed)
        {
            scale = tuning.UpMaxSpeed / speed;
        }
        else
        {
            var excess = speed - tuning.UpResistSpeed;
            var decayedExcess = excess * (1f - tuning.UpResistFactor);
            var newSpeed = tuning.UpResistSpeed + decayedExcess;
            scale = newSpeed / speed;
        }

        return velocity with { Z = velocity.Z * scale };
    }

    /// <summary>
    /// Buoyancy. Structurally present (call-site and ordering match <c>PlayerPhysics.md</c>) but
    /// inert while <paramref name="waterCoverage"/> is 0 — this port has no liquid-volume
    /// detection yet, so it never has anything other than 0 to pass. The formula shape
    /// (buoyant accel opposing gravity, scaled by coverage and datablock <c>density</c>) is a
    /// placeholder, NOT verified against the disassembly; <c>PlayerPhysics.md</c> records only
    /// that this stage exists and where it sits in the order, nothing about its contents.
    /// </summary>
    internal static PhysVector3 ApplyBuoyancy(PhysVector3 velocity, PlayerTuning tuning, float gravity, float dt, float waterCoverage)
    {
        if (waterCoverage <= 0f) return velocity;

        var buoyantAccel = -gravity * waterCoverage * tuning.Density;
        return velocity with { Z = velocity.Z + buoyantAccel * dt };
    }

    /// <summary>
    /// Drag. <c>PlayerPhysics.md</c> is explicit and verified on one point: <c>mDrag</c> is zero
    /// out of water, so this must be a no-op whenever <paramref name="waterCoverage"/> is 0 —
    /// which is unconditionally true in this port today (see <see cref="ApplyBuoyancy"/>). The
    /// in-water scaling below is otherwise unverified filler, present only to keep the call site
    /// and ordering faithful.
    /// </summary>
    internal static PhysVector3 ApplyDrag(PhysVector3 velocity, PlayerTuning tuning, float waterCoverage)
    {
        if (waterCoverage <= 0f) return velocity; // verified: land behaviour must be a no-op here.

        var dragScale = 1f - tuning.Drag * waterCoverage;
        return velocity * MathF.Max(0f, dragScale);
    }
}
