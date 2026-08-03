namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// One tick's worth of resolved player movement input, as <see cref="PlayerForcePhase"/> needs
/// it.
/// </summary>
/// <remarks>
/// This is deliberately <b>not</b> <c>Sim.Process.Move</c>. <c>Move</c>'s field layout (movement
/// axes, view angles, trigger bits) has not been recovered — <c>Sim/Process/Move.cs</c> records
/// only its 64-byte wire stride and is otherwise an opaque placeholder by design, and
/// <c>Sim/Process</c> is explicitly off-limits for this work (concurrent effort elsewhere). This
/// type exists so the force phase can be built and tested against a resolved, physics-shaped
/// input without waiting on that recovery.
/// <para>
/// <b>Assumption:</b> <see cref="MoveDirection"/> is taken as an already world-space horizontal
/// direction (the caller has done whatever <c>Player::getTransform()</c> heading resolution the
/// real engine applies to the move's local forward/side axes). Reproducing that resolution needs
/// the player's own orientation state, which is out of scope here — nothing in
/// <c>PlayerPhysics.md</c> describes it, and the recovered functions only reach as far as
/// <c>mVelocity</c>/<c>updatePos</c>, not orientation. Magnitude is meaningful only up to 1 (a
/// direction the caller has already scaled by input throttle); this simplification does not
/// affect any of the "facts that must not be got wrong" in <c>PlayerPhysics.md</c>, all of which
/// live downstream of this input.
/// </para>
/// </remarks>
public readonly record struct PlayerMoveInput(
    PhysVector3 MoveDirection,
    bool Jump,
    bool Jet)
{
    public static readonly PlayerMoveInput None = new(PhysVector3.Zero, Jump: false, Jet: false);
}
