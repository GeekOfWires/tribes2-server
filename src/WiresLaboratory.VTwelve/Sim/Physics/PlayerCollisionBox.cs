namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// The player's collision volume, axis-aligned, positioned relative to
/// <see cref="PlayerPhysicsState.Position"/> — a box, not the real engine's
/// head/torso-percentage-split shape (see <c>boxHeadPercentage</c> etc. in the TSV), since
/// nothing in <c>PlayerPhysics.md</c> describes how <c>findContact</c> uses that split and
/// <see cref="ICollisionSurface"/> is explicitly a stand-in for the not-yet-recovered
/// <c>ExtrudedPolyList</c> clipper (see that interface's remarks).
/// </summary>
/// <param name="HalfWidth">Half-extent along X and Y (a square footprint), in world units.</param>
/// <param name="MinZ">
/// Bottom of the box relative to <see cref="PlayerPhysicsState.Position"/>. 0 if position is the
/// feet/ground-contact origin, as it conventionally is for a Torque-lineage <c>Player</c>.
/// </param>
/// <param name="MaxZ">Top of the box relative to <see cref="PlayerPhysicsState.Position"/> — i.e. eye-ish height.</param>
public readonly record struct PlayerCollisionBox(float HalfWidth, float MinZ, float MaxZ);
