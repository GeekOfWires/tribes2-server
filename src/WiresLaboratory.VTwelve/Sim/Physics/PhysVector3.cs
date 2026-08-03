namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// A single-precision 3-component vector for the player movement simulation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not reuse <c>Script.Console.ConsoleVector3</c>:</b> that type is the parsed/typed side
/// of a TorqueScript console string (<c>"x y z"</c>) — its constructor path runs through
/// <c>ConsoleValue</c> parsing and it carries an implicit conversion back to
/// <c>ConsoleValue</c>. None of that belongs on the hot path of a per-tick physics integrator,
/// and pulling the simulation math into the <c>Script.Console</c> namespace would tie this
/// deliberately console-free physics code to the script layer for no benefit. This type exists
/// so <c>Sim/Physics</c> has its own dependency-free vector rather than either coupling to
/// <c>ConsoleVector3</c> or editing it to fit a use case it wasn't designed for.
/// </para>
/// <para>
/// Every component is <see langword="float"/>, not <see langword="double"/>, deliberately: the
/// original engine is a 32-bit x87-era single-precision binary, and the project's standing rule
/// (see <c>PlayerPhysics.md</c>) is that using <see langword="double"/> anywhere in this
/// simulation silently diverges from what the client predicts.
/// </para>
/// </remarks>
public readonly record struct PhysVector3(float X, float Y, float Z)
{
    public static readonly PhysVector3 Zero = new(0f, 0f, 0f);
    public static readonly PhysVector3 UnitZ = new(0f, 0f, 1f);

    public static PhysVector3 operator +(PhysVector3 a, PhysVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static PhysVector3 operator -(PhysVector3 a, PhysVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static PhysVector3 operator -(PhysVector3 a) => new(-a.X, -a.Y, -a.Z);

    public static PhysVector3 operator *(PhysVector3 a, float scale) => new(a.X * scale, a.Y * scale, a.Z * scale);

    public static PhysVector3 operator *(float scale, PhysVector3 a) => a * scale;

    /// <summary>The horizontal (X/Y only, Z zeroed) projection — used by the horizontal resist stage.</summary>
    public PhysVector3 Horizontal => new(X, Y, 0f);

    public float LengthSquared => X * X + Y * Y + Z * Z;

    public float Length => MathF.Sqrt(LengthSquared);

    public static float Dot(PhysVector3 a, PhysVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Unit-length copy, or <see cref="Zero"/> for a (near-)zero vector rather than propagating NaN or a divide-by-zero.</summary>
    public PhysVector3 Normalized()
    {
        var len = Length;
        return len > 1e-8f ? this * (1f / len) : Zero;
    }

    /// <summary>
    /// Explicit override, deliberately not left to the compiler-synthesized record <c>ToString</c>/
    /// <c>PrintMembers</c>. On this SDK (net10.0), the auto-generated <c>PrintMembers</c> picks up
    /// <see cref="Horizontal"/> — an expression-bodied property whose type is <c>PhysVector3</c>
    /// itself — as a member to print, which formats it by calling <c>ToString()</c> on it, which
    /// calls <c>PrintMembers</c> again: unconditional infinite recursion, observed as a real stack
    /// overflow (confirmed with a standalone repro reduced to a bare <c>record struct</c> with one
    /// self-typed property; not specific to anything else in this type). Defining <c>ToString</c>
    /// explicitly replaces the synthesized member entirely and sidesteps it.
    /// </summary>
    public override string ToString() => $"({X}, {Y}, {Z})";
}
