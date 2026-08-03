namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// The position phase — the managed analogue of <c>Player::updatePos(F32 travelTime) -&gt; bool</c>
/// (<c>0x005d7220</c>/<c>0x1ab0</c>, per <c>PlayerPhysics.md</c>). Integrates position from the
/// velocity <see cref="PlayerForcePhase"/> already finished, resolving collisions against an
/// <see cref="ICollisionSurface"/> along the way. Takes a plain travel time, not a move — the
/// engine function's actual signature, and the reason this is a separate phase from
/// <see cref="PlayerForcePhase"/> rather than one combined step.
/// </summary>
public static class PlayerPositionPhase
{
    /// <summary>Retry loop cap. On exhaustion the entire tick is rolled back. Verified, <c>PlayerPhysics.md</c>.</summary>
    public const int MaxRetries = 5;

    /// <summary>The collision response's separation bias — the <c>+ 0.01f</c> in <c>v += n * (-(v . n) + 0.01f)</c>. Verified, <c>PlayerPhysics.md</c>.</summary>
    public const float SeparationBias = 0.01f;

    private const float TimeEpsilon = 1e-6f;
    private const float SpeedEpsilon = 1e-6f;

    /// <summary>
    /// Integrates <paramref name="state"/> across <paramref name="travelTime"/> seconds, resolving
    /// collisions against <paramref name="surface"/>. Returns <see langword="false"/> if the
    /// 5-retry cap was exhausted, in which case the entire tick was rolled back: position
    /// restored, velocity zeroed (verified, <c>PlayerPhysics.md</c>) — the caller does not need
    /// to do anything further to recover.
    /// </summary>
    /// <param name="state">Mutated in place: <see cref="PlayerPhysicsState.Position"/>, <see cref="PlayerPhysicsState.Velocity"/>, <see cref="PlayerPhysicsState.OnGround"/>, <see cref="PlayerPhysicsState.ContactNormal"/>.</param>
    /// <param name="surface">Stands in for the not-yet-recovered <c>ExtrudedPolyList</c> clipper — see <see cref="ICollisionSurface"/>.</param>
    /// <param name="box">The player's collision volume for this sweep.</param>
    /// <param name="tuning">Used only for <see cref="PlayerTuning.RunSurfaceAngleCosine"/>, to decide which contacts count as "ground" for <see cref="PlayerPhysicsState.OnGround"/>.</param>
    /// <param name="travelTime">Seconds to integrate across — <see cref="PhysicsWorld.TickSeconds"/> for a normal tick.</param>
    /// <param name="groundDisplacement">
    /// This tick's animation-driven ground/platform displacement, if any. See the deliberate
    /// X/Y-only quirk below. Defaults to <see cref="PhysVector3.Zero"/> — this port does not run
    /// shape animation trees (<c>PlayerPhysics.md</c>, "Gaps"), so nothing currently produces a
    /// non-zero value; the parameter exists so the quirk has somewhere to be exercised once
    /// something upstream does.
    /// </param>
    /// <returns><see langword="true"/> if the tick completed normally; <see langword="false"/> if it was rolled back.</returns>
    public static bool Integrate(
        PlayerPhysicsState state,
        ICollisionSurface surface,
        PlayerCollisionBox box,
        PlayerTuning tuning,
        float travelTime,
        PhysVector3 groundDisplacement = default)
    {
        // Quirk, preserved deliberately (PlayerPhysics.md, "Gaps" / "Observed quirk, recorded as
        // found", confirmed twice in the disassembly at 0x5d4e90): the zero-test before applying
        // ground displacement checks ONLY .X and .Y. A pure-Z displacement (e.g. a platform
        // moving straight up with no horizontal motion) is silently dropped — the position is
        // NOT adjusted even though the platform moved. Do not "fix" this to `!= PhysVector3.Zero`;
        // that reads as more correct and is exactly the divergence PlayerPhysics.md warns about.
        if (groundDisplacement.X != 0f || groundDisplacement.Y != 0f)
        {
            state.Position += groundDisplacement;
        }

        var originalPosition = state.Position;
        var originalVelocity = state.Velocity;
        var originalOnGround = state.OnGround;
        var originalContactNormal = state.ContactNormal;

        var remainingTime = travelTime;
        var iterations = 0;
        var groundContactThisTick = false;
        var lastGroundNormal = state.ContactNormal;

        while (remainingTime > TimeEpsilon && iterations < MaxRetries)
        {
            iterations++;
            var moveTime = remainingTime;
            var motion = state.Velocity * moveTime;

            if (!surface.TryFindContact(box, state.Position, motion, out var contactTime, out var normal, out _))
            {
                // No contact: the full remaining budget is free flight.
                state.Position += motion;
                remainingTime = 0f;
                break;
            }

            // Advance to the contact point.
            state.Position += motion * contactTime;
            var consumedTime = moveTime * contactTime;
            remainingTime -= consumedTime;

            // Collision response: v += n * (-(v . n) + 0.01f). No restitution, no friction —
            // verified, PlayerPhysics.md.
            var normalSpeed = PhysVector3.Dot(state.Velocity, normal);
            state.Velocity += normal * (-normalSpeed + SeparationBias);

            if (normal.Z > tuning.RunSurfaceAngleCosine)
            {
                groundContactThisTick = true;
                lastGroundNormal = normal;
            }

            // The 1cm back-off — NOT time-symmetric (verified, PlayerPhysics.md): position is
            // pulled back by min(0.01 / speed, moveTime) worth of travel along the (now
            // post-response) velocity, but that time is deliberately NOT added back to
            // `remainingTime`. An implementation that credited it back would look more
            // "correct" and would diverge from the client — do not add it back.
            var speed = state.Velocity.Length;
            if (speed > SpeedEpsilon)
            {
                var backoffTime = MathF.Min(SeparationBias / speed, moveTime);
                state.Position -= state.Velocity * backoffTime;
            }
        }

        if (remainingTime > TimeEpsilon && iterations >= MaxRetries)
        {
            // Retry cap exhausted: roll back the ENTIRE tick. Verified, PlayerPhysics.md.
            state.Position = originalPosition;
            state.Velocity = PhysVector3.Zero;
            state.OnGround = originalOnGround;
            state.ContactNormal = originalContactNormal;
            return false;
        }

        state.OnGround = groundContactThisTick;
        if (groundContactThisTick) state.ContactNormal = lastGroundNormal;
        return true;
    }
}
