namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// The console math starter set: vector algebra plus 4x4 matrix construction/composition,
/// implemented for real rather than stubbed, because physics depends on them and — unlike
/// almost everything else in the manifest — they are self-contained. They need no SimObject
/// model and no datablock field registry, both of which ReverseEngineeringNotes.md records as
/// still unlocated in the binary.
/// </summary>
/// <remarks>
/// Names, namespace shape and argument counts come straight from EngineConsoleSurface.md:
/// <c>VectorCross(vec1,vec2)</c>, <c>VectorDist(vec1,vec2)</c>, <c>VectorDot(vec1,vec2)</c>,
/// <c>VectorLen(vec)</c>, <c>VectorNormalize(vec)</c>, <c>VectorScale(vec,scaler)</c>,
/// <c>VectorSub(vec1,vec2)</c>, <c>MatrixCreate(Pos, Rot)</c>, <c>MatrixMultiply(Left, Right)</c>,
/// <c>MatrixMulPoint(transform, point)</c>, <c>MatrixMulVector(transform, vector)</c> — all
/// global functions, all two-argument except the two single-vector functions.
/// <para>
/// <c>VectorAdd</c> is the one exception: it is registered here because it was explicitly
/// requested and is harmless and self-contained (a straightforward mirror of
/// <c>VectorSub</c>'s argument shape), but it does <b>not</b> appear anywhere in
/// EngineConsoleSurface.md's 304 recovered entries. It therefore will never show up as "missing"
/// in a <see cref="ConsoleRegistry.GetCoverage"/> report against that manifest, but it also does
/// not count toward the 304 — it is simply extra.
/// </para>
/// </remarks>
public static class VectorMathConsoleFunctions
{
    public static void Register(ConsoleRegistry registry)
    {
        registry.RegisterGlobalFunction("VectorAdd", 2, 2, "VectorAdd(vec1,vec2)", (_, args) =>
            (Vec(args, 0) + Vec(args, 1)).ToConsoleValue());

        registry.RegisterGlobalFunction("VectorSub", 2, 2, "VectorSub(vec1,vec2)", (_, args) =>
            (Vec(args, 0) - Vec(args, 1)).ToConsoleValue());

        registry.RegisterGlobalFunction("VectorScale", 2, 2, "VectorScale(vec,scaler)", (_, args) =>
            (Vec(args, 0) * args[1].AsFloat()).ToConsoleValue());

        registry.RegisterGlobalFunction("VectorDot", 2, 2, "VectorDot(vec1,vec2)", (_, args) =>
            new ConsoleValue(ConsoleVector3.Dot(Vec(args, 0), Vec(args, 1))));

        registry.RegisterGlobalFunction("VectorCross", 2, 2, "VectorCross(vec1,vec2)", (_, args) =>
            ConsoleVector3.Cross(Vec(args, 0), Vec(args, 1)).ToConsoleValue());

        registry.RegisterGlobalFunction("VectorNormalize", 1, 1, "VectorNormalize(vec)", (_, args) =>
            Vec(args, 0).Normalized().ToConsoleValue());

        registry.RegisterGlobalFunction("VectorLen", 1, 1, "VectorLen(vec)", (_, args) =>
            new ConsoleValue(Vec(args, 0).Length));

        registry.RegisterGlobalFunction("VectorDist", 2, 2, "VectorDist(vec1,vec2)", (_, args) =>
            new ConsoleValue((Vec(args, 0) - Vec(args, 1)).Length));

        registry.RegisterGlobalFunction("MatrixCreate", 2, 2, "MatrixCreate(Pos, Rot)", (_, args) =>
            ConsoleMatrix.CreateFromPositionAndAxisAngle(Vec(args, 0), args[1]));

        registry.RegisterGlobalFunction("MatrixMultiply", 2, 2, "MatrixMultiply(Left, Right)", (_, args) =>
            ConsoleMatrix.Multiply(ConsoleMatrix.Parse(args[0]), ConsoleMatrix.Parse(args[1])));

        registry.RegisterGlobalFunction("MatrixMulVector", 2, 2, "MatrixMulVector(transform, vector)", (_, args) =>
            ConsoleMatrix.Parse(args[0]).MulVector(Vec(args, 1)).ToConsoleValue());

        registry.RegisterGlobalFunction("MatrixMulPoint", 2, 2, "MatrixMulPoint(transform, point)", (_, args) =>
            ConsoleMatrix.Parse(args[0]).MulPoint(Vec(args, 1)).ToConsoleValue());
    }

    private static ConsoleVector3 Vec(IReadOnlyList<ConsoleValue> args, int index) => ConsoleVector3.Parse(args[index]);
}
