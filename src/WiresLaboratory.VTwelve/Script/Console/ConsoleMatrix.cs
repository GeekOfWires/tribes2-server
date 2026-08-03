namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// A 4x4 affine transform as TorqueScript is presumed to pass it: 16 space-separated floats,
/// row-major, with translation in the last row — a row-vector convention, so a point transforms
/// as <c>p' = p * M</c> and <c>MatrixMultiply(Left, Right)</c> composes as "apply Left, then
/// apply Right".
/// </summary>
/// <remarks>
/// <para>
/// This layout is <b>inferred, not recovered</b>. EngineConsoleSurface.md proves that
/// <c>MatrixCreate</c>, <c>MatrixMultiply</c>, <c>MatrixMulPoint</c> and <c>MatrixMulVector</c>
/// exist and how many arguments each takes; it says nothing about how a matrix value is laid
/// out as a string. Row-vector-with-trailing-translation is the layout the Torque engine
/// lineage is documented to use elsewhere (it targets Direct3D, which favours row vectors), and
/// every method here is internally consistent with that choice — but per
/// ReverseEngineeringNotes.md the datablock/transform field offsets in this specific T2 binary
/// are still unlocated, so there is no live memory evidence to check this against. Treat exact
/// numeric output as provisional until that changes.
/// </para>
/// <para>
/// <see cref="CreateFromPositionAndAxisAngle"/> reads the rotation argument as an axis-angle
/// 4-tuple ("x y z angle", radians) rather than Euler angles. The evidence for that reading is
/// indirect but real: the manifest separately lists <c>MatrixCreateFromEuler("x y z")</c>, and a
/// plain <c>MatrixCreate(Pos, Rot)</c> that also took Euler angles would make that second
/// function redundant.
/// </para>
/// </remarks>
public sealed class ConsoleMatrix
{
    private readonly float[] _m; // row-major, _m[row * 4 + col]

    private ConsoleMatrix(float[] m) => _m = m;

    public static ConsoleMatrix Identity { get; } = FromRows(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    public float this[int row, int col] => _m[row * 4 + col];

    private static ConsoleMatrix FromRows(
        float m00, float m01, float m02, float m03,
        float m10, float m11, float m12, float m13,
        float m20, float m21, float m22, float m23,
        float m30, float m31, float m32, float m33) =>
        new([m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33]);

    /// <summary>Rodrigues' rotation formula around <paramref name="pos"/>, laid out for row-vector application.</summary>
    public static ConsoleMatrix CreateFromPositionAndAxisAngle(ConsoleVector3 pos, ConsoleValue rot)
    {
        var tokens = rot.Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        float Component(int i) => i < tokens.Length ? new ConsoleValue(tokens[i]).AsFloat() : 0f;
        var axis = new ConsoleVector3(Component(0), Component(1), Component(2)).Normalized();
        var angle = Component(3);

        if (axis == ConsoleVector3.Zero)
        {
            // Degenerate axis (all zeros, or too short to normalize): no well-defined rotation.
            // Falling back to identity rotation keeps the position meaningful instead of
            // propagating NaN through the trig below.
            return FromRows(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                pos.X, pos.Y, pos.Z, 1);
        }

        var sin = MathF.Sin(angle);
        var cos = MathF.Cos(angle);
        var t = 1f - cos;
        var (x, y, z) = (axis.X, axis.Y, axis.Z);

        return FromRows(
            t * x * x + cos, t * x * y + sin * z, t * x * z - sin * y, 0,
            t * x * y - sin * z, t * y * y + cos, t * y * z + sin * x, 0,
            t * x * z + sin * y, t * y * z - sin * x, t * z * z + cos, 0,
            pos.X, pos.Y, pos.Z, 1);
    }

    /// <summary>Parses 16 space-separated floats; a short string reads missing trailing components as 0.</summary>
    public static ConsoleMatrix Parse(ConsoleValue value)
    {
        var tokens = value.Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var m = new float[16];
        for (var i = 0; i < 16; i++)
            m[i] = i < tokens.Length ? new ConsoleValue(tokens[i]).AsFloat() : 0f;
        return new ConsoleMatrix(m);
    }

    public string Format() => string.Join(' ', Array.ConvertAll(_m, ConsoleValue.FormatFloat));

    public static implicit operator ConsoleValue(ConsoleMatrix matrix) => new(matrix.Format());

    public static ConsoleMatrix Multiply(ConsoleMatrix left, ConsoleMatrix right)
    {
        var result = new float[16];
        for (var row = 0; row < 4; row++)
        for (var col = 0; col < 4; col++)
        {
            var sum = 0f;
            for (var k = 0; k < 4; k++) sum += left[row, k] * right[k, col];
            result[row * 4 + col] = sum;
        }
        return new ConsoleMatrix(result);
    }

    /// <summary>Transforms a direction: the rotation/scale block only, ignoring translation.</summary>
    public ConsoleVector3 MulVector(ConsoleVector3 v) => new(
        v.X * this[0, 0] + v.Y * this[1, 0] + v.Z * this[2, 0],
        v.X * this[0, 1] + v.Y * this[1, 1] + v.Z * this[2, 1],
        v.X * this[0, 2] + v.Y * this[1, 2] + v.Z * this[2, 2]);

    /// <summary>Transforms a position: the rotation/scale block plus the translation row.</summary>
    public ConsoleVector3 MulPoint(ConsoleVector3 p) => MulVector(p) + new ConsoleVector3(this[3, 0], this[3, 1], this[3, 2]);
}
