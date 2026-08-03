namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// The per-tick simulation state <see cref="PlayerForcePhase"/> and <see cref="PlayerPositionPhase"/>
/// read and mutate — the managed equivalent of the subset of <c>Player</c>'s member fields the
/// movement/collision path touches (<c>mVelocity</c>, its ground-contact bookkeeping, energy,
/// jump cooldown).
/// </summary>
public sealed class PlayerPhysicsState
{
    public PhysVector3 Position { get; set; }

    public PhysVector3 Velocity { get; set; }

    /// <summary>Whether the last <see cref="PlayerPositionPhase"/> pass ended in surface contact.</summary>
    public bool OnGround { get; set; }

    /// <summary>The contact surface normal from the last ground contact, meaningful only while <see cref="OnGround"/>.</summary>
    public PhysVector3 ContactNormal { get; set; } = PhysVector3.UnitZ;

    /// <summary>Current energy, drained by running/jumping/jetting and gated by each action's <c>min*Energy</c> tunable.</summary>
    public float Energy { get; set; }

    /// <summary>
    /// Ticks remaining before another jump may fire — set to <c>PlayerData.jumpDelay</c> on
    /// jumping and decremented once per <see cref="PlayerForcePhase.ApplyMove"/> call.
    /// </summary>
    public int JumpCooldownTicks { get; set; }

    /// <summary>Creates a state at rest, at <paramref name="position"/>, with energy full per <paramref name="tuning"/>.</summary>
    public static PlayerPhysicsState AtRest(PhysVector3 position, PlayerTuning tuning) => new()
    {
        Position = position,
        Velocity = PhysVector3.Zero,
        OnGround = false,
        Energy = tuning.MaxEnergy,
        JumpCooldownTicks = 0,
    };
}
