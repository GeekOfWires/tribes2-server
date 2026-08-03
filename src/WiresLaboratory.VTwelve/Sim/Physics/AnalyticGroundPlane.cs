namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// A flat, infinite ground plane at a fixed Z — the simplest possible <see cref="ICollisionSurface"/>,
/// for exercising and testing <see cref="PlayerPositionPhase"/> without the real (not yet
/// recovered) <c>ExtrudedPolyList</c> clipper. See <see cref="ICollisionSurface"/>'s remarks for
/// why this stands in and what it does not attempt to reproduce.
/// </summary>
/// <param name="groundZ">World Z of the plane. The box's <see cref="PlayerCollisionBox.MinZ"/>-offset bottom rests here.</param>
public sealed class AnalyticGroundPlane(float groundZ) : ICollisionSurface
{
    private static readonly PhysVector3 UpNormal = PhysVector3.UnitZ;

    /// <summary>World Z of the plane, as passed to the constructor.</summary>
    public float GroundZ { get; } = groundZ;

    public bool TryFindContact(
        PlayerCollisionBox box,
        PhysVector3 start,
        PhysVector3 motion,
        out float contactTime,
        out PhysVector3 normal,
        out float maxHeight)
    {
        normal = UpNormal;
        maxHeight = GroundZ;

        var bottomStart = start.Z + box.MinZ;
        var bottomEnd = bottomStart + motion.Z;

        // Already at or below the plane: contact immediately. This also covers "resting on the
        // ground with zero vertical motion" (motion.Z == 0, bottomStart <= GroundZ), which is
        // the steady-state case a standing/running player is in on every tick.
        if (bottomStart <= GroundZ)
        {
            contactTime = 0f;
            return true;
        }

        // Moving away from or parallel to the plane: never reaches it.
        if (motion.Z >= 0f)
        {
            contactTime = default;
            return false;
        }

        // Linear interpolation for the fraction of `motion` at which bottomStart + motion.Z * t == GroundZ.
        var t = (GroundZ - bottomStart) / motion.Z;
        if (t is < 0f or > 1f)
        {
            contactTime = default;
            return false;
        }

        contactTime = t;
        return bottomEnd <= GroundZ; // sanity: t in range should already guarantee this
    }
}
