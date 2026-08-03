namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// A 3-component vector as TorqueScript passes it: a single space-separated string,
/// <c>"x y z"</c> — the shape every vector-typed argument in EngineConsoleSurface.md uses (e.g.
/// <c>ContainerRayCast("x y z", "x y z", mask, ...)</c>, <c>alxListener3f(ALenum, "x y z" | x, y, z)</c>).
/// This type is the parsed/typed side of that string; <see cref="ConsoleValue"/> stays the
/// canonical wire form.
/// </summary>
public readonly record struct ConsoleVector3(float X, float Y, float Z)
{
    public static readonly ConsoleVector3 Zero = new(0f, 0f, 0f);

    /// <summary>
    /// Splits <paramref name="value"/> on whitespace and reads up to three components. Each
    /// token goes through <see cref="ConsoleValue.AsFloat"/>, so a missing or malformed
    /// component reads as 0 — the same "bad input degrades to zero, never throws" rule every
    /// other console coercion follows — rather than a vector-specific parse failure.
    /// </summary>
    public static ConsoleVector3 Parse(ConsoleValue value)
    {
        var tokens = value.Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new ConsoleVector3(Component(tokens, 0), Component(tokens, 1), Component(tokens, 2));
    }

    private static float Component(string[] tokens, int index) =>
        index < tokens.Length ? new ConsoleValue(tokens[index]).AsFloat() : 0f;

    public static ConsoleVector3 operator +(ConsoleVector3 a, ConsoleVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static ConsoleVector3 operator -(ConsoleVector3 a, ConsoleVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static ConsoleVector3 operator *(ConsoleVector3 a, float scale) => new(a.X * scale, a.Y * scale, a.Z * scale);

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public static float Dot(ConsoleVector3 a, ConsoleVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static ConsoleVector3 Cross(ConsoleVector3 a, ConsoleVector3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>Unit-length copy, or <see cref="Zero"/> for a (near-)zero vector rather than propagating NaN.</summary>
    public ConsoleVector3 Normalized()
    {
        var len = Length;
        return len > 1e-8f ? this * (1f / len) : Zero;
    }

    public ConsoleValue ToConsoleValue() =>
        new($"{ConsoleValue.FormatFloat(X)} {ConsoleValue.FormatFloat(Y)} {ConsoleValue.FormatFloat(Z)}");

    public static implicit operator ConsoleValue(ConsoleVector3 vector) => vector.ToConsoleValue();
}
