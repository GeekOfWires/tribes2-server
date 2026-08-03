namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// A single contact found by sweeping the player's collision box along a motion vector — the
/// interface <see cref="PlayerPositionPhase"/> consumes in place of the real engine's
/// <c>ExtrudedPolyList</c>/<c>buildPolyList</c>/<c>extrude</c> clipper.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the largest documented gap.</b> Per <c>PlayerPhysics.md</c> ("Gaps"):
/// <c>Player::updatePos</c>'s control flow (the retry loop, the collision response, the 1cm
/// back-off) is fully recovered, but every decision it makes depends on the contact time and
/// normal the real clipper produces, and the clipper itself (<c>ExtrudedPolyList</c>'s
/// <c>extrude</c>/<c>buildPolyList</c>) has not been analysed. The structure implemented against
/// this interface is reproducible today; <b>the contact times and normals a real implementation
/// of this interface returns are NOT yet bit-exact against the shipped client</b> — that
/// recovery is still pending. Anything built on <see cref="ICollisionSurface"/> inherits that
/// caveat until an implementation backed by the recovered clipper replaces
/// <see cref="AnalyticGroundPlane"/>.
/// </para>
/// <para>
/// <see cref="TryFindContact"/>'s contract (motion as a full-tick displacement, contact time as
/// a <c>[0,1]</c> fraction of it) is this project's own convention for describing a swept
/// contact, chosen because it composes cleanly with <see cref="PlayerPositionPhase"/>'s retry
/// loop; it is not itself recovered from the disassembly (only the loop shape that consumes it
/// is).
/// </para>
/// </remarks>
public interface ICollisionSurface
{
    /// <summary>
    /// Sweeps <paramref name="box"/> from <paramref name="start"/> along <paramref name="motion"/>
    /// (the full displacement for this sub-step, not a direction) and reports the first contact,
    /// if any.
    /// </summary>
    /// <param name="box">The player's collision volume, positioned relative to <paramref name="start"/>.</param>
    /// <param name="start">The box's position at the start of the sweep.</param>
    /// <param name="motion">The full displacement to sweep across — <c>velocity * moveTime</c> for this sub-step.</param>
    /// <param name="contactTime">
    /// On a contact, the fraction of <paramref name="motion"/> (in <c>[0,1]</c>) at which contact
    /// occurs — 0 if the box already touches or penetrates the surface at <paramref name="start"/>.
    /// Undefined when this method returns <see langword="false"/>.
    /// </param>
    /// <param name="normal">On a contact, the surface normal at the contact point (unit length, pointing away from the surface).</param>
    /// <param name="maxHeight">
    /// On a contact, the height of the contacted surface at the contact point — the quantity
    /// <c>Player::findContact</c> compares against <c>maxStepHeight</c> to decide whether the
    /// player can step up onto it rather than being blocked. Undefined when this method returns
    /// <see langword="false"/>.
    /// </param>
    /// <returns><see langword="true"/> if the swept box contacts a surface before covering the full <paramref name="motion"/>.</returns>
    bool TryFindContact(
        PlayerCollisionBox box,
        PhysVector3 start,
        PhysVector3 motion,
        out float contactTime,
        out PhysVector3 normal,
        out float maxHeight);
}
