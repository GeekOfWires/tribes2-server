namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// World-level parameters the player movement model reads but does not own.
/// </summary>
/// <remarks>
/// <b>Evidence, from <c>PlayerPhysics.md</c>:</b>
/// <list type="bullet">
/// <item>
/// The movement timestep is the constant at <c>0x0079c140</c> = <c>0.03125f</c>, verified —
/// exact binary 1/32, not the <c>0.032f</c> that a different, unrelated <c>ShapeBase</c> timer
/// accumulator uses elsewhere on the tick path. Using <c>0.032</c> here is a documented,
/// specifically-called-out mistake: a 2.4% per-tick error against the client.
/// </item>
/// <item>
/// Gravity is <c>-20.0f</c>, stored at <c>0x007a1a20</c>, verified — and it is a
/// <b>mutable global</b> in the engine, not a compile-time constant. A faithful server reads it
/// rather than hard-coding the value, hence <see cref="Gravity"/> is a settable instance
/// property (defaulted to the documented value) instead of a <see langword="const"/>.
/// </item>
/// </list>
/// </remarks>
public sealed class PhysicsWorld
{
    /// <summary>
    /// The movement tick length in seconds: exact binary 1/32. See the type remarks — do not
    /// replace this with <c>0.032f</c>, which is a different, unrelated constant in the engine.
    /// </summary>
    public const float TickSeconds = 0.03125f;

    /// <summary>
    /// Gravitational acceleration along Z, in world units/s^2. Defaults to the verified engine
    /// value (<c>-20.0f</c>) but is deliberately mutable — the engine stores this as a global
    /// that can change at runtime (e.g. a mission-level override), not a compiled-in constant.
    /// </summary>
    public float Gravity { get; set; } = -20.0f;
}
