namespace WiresLaboratory.VTwelve.Script.Dso;

/// <summary>
/// A compiled TorqueScript code block (<c>*.cs.dso</c>) as shipped by V12 / Tribes 2.
/// </summary>
/// <remarks>
/// <para>
/// Layout, as observed in the shipped files (version 174):
/// </para>
/// <code>
///   u32                version                 (174 for V12 / Tribes 2)
///   u32 + bytes        global string table     (NUL-separated, indexed by byte offset)
///   u32 + f64[]        global float table
///   u32 + bytes        function string table
///   u32 + f64[]        function float table
///   u32                code size (instruction slots)
///   u32                line/break pair count
///   codeSize x         instruction slot: one byte, or 0xFF followed by a u32
///   pairs x 2 x u32    line/break table
///   u32                identifier patch entry count
///     per entry:       u32 string-table offset, u32 patch count, then that many u32 slot indices
/// </code>
/// <para>
/// The identifier table rewrites slots in the instruction stream to string-table offsets,
/// which is why patching happens during load rather than at execution time.
/// </para>
/// <para>
/// Parsing is strict: the reader must land exactly on the end of the file. That is the
/// verification criterion for the whole layout — a wrong field width or a missing table
/// desynchronises the stream and is caught immediately rather than producing plausible junk.
/// </para>
/// </remarks>
public sealed class DsoFile
{
    /// <summary>DSO format version shipped by Tribes 2 / V12.</summary>
    public const uint V12Version = 174;

    private DsoFile(
        uint version,
        DsoStringTable globalStrings, double[] globalFloats,
        DsoStringTable functionStrings, double[] functionFloats,
        uint[] code, uint codeSize, uint[] lineBreakPairs, IdentifierPatch[] identifiers)
    {
        Version = version;
        GlobalStrings = globalStrings;
        GlobalFloats = globalFloats;
        FunctionStrings = functionStrings;
        FunctionFloats = functionFloats;
        Code = code;
        CodeSize = codeSize;
        LineBreakPairs = lineBreakPairs;
        Identifiers = identifiers;
    }

    public uint Version { get; }
    public DsoStringTable GlobalStrings { get; }
    public double[] GlobalFloats { get; }
    public DsoStringTable FunctionStrings { get; }
    public double[] FunctionFloats { get; }

    /// <summary>Decoded instruction slots (identifier patches already applied).</summary>
    public uint[] Code { get; }

    /// <summary>Number of leading <see cref="Code"/> entries that are instruction slots.</summary>
    public uint CodeSize { get; }

    public uint[] LineBreakPairs { get; }
    public IdentifierPatch[] Identifiers { get; }

    /// <summary>An identifier fix-up: a string-table offset written into given code slots.</summary>
    public readonly record struct IdentifierPatch(uint StringOffset, uint[] Slots);

    public static DsoFile Parse(ReadOnlySpan<byte> data, string? origin = null)
    {
        var r = new SpanReader(data, origin);

        var version = r.ReadUInt32();
        var globalStrings = DsoStringTable.Read(ref r);
        var globalFloats = ReadFloatTable(ref r);
        var functionStrings = DsoStringTable.Read(ref r);
        var functionFloats = ReadFloatTable(ref r);

        var codeSize = r.ReadUInt32();
        var lineBreakPairCount = r.ReadUInt32();

        // Instruction slots are byte-compacted: 0xFF escapes to a full u32.
        var total = checked(codeSize + lineBreakPairCount * 2);
        var code = new uint[total];
        for (var i = 0u; i < codeSize; i++)
        {
            var b = r.ReadByte();
            code[i] = b == 0xFF ? r.ReadUInt32() : b;
        }
        for (var i = codeSize; i < total; i++)
            code[i] = r.ReadUInt32();

        var lineBreakPairs = new uint[lineBreakPairCount * 2];
        Array.Copy(code, codeSize, lineBreakPairs, 0, lineBreakPairs.Length);

        var identCount = r.ReadUInt32();
        var identifiers = new IdentifierPatch[identCount];
        for (var i = 0u; i < identCount; i++)
        {
            var offset = r.ReadUInt32();
            var count = r.ReadUInt32();
            var slots = new uint[count];
            for (var j = 0u; j < count; j++)
            {
                var slot = r.ReadUInt32();
                if (slot >= codeSize)
                    throw new DsoFormatException(origin, $"identifier patch targets slot {slot} beyond code size {codeSize}");
                slots[j] = slot;
                code[slot] = offset;      // the engine patches in place at load time
            }
            identifiers[i] = new IdentifierPatch(offset, slots);
        }

        // Strictness is the whole point: landing anywhere but the end means the layout is wrong.
        if (!r.AtEnd)
            throw new DsoFormatException(origin, $"{r.Remaining} trailing byte(s) after the identifier table");

        return new DsoFile(version, globalStrings, globalFloats, functionStrings, functionFloats,
                           code, codeSize, lineBreakPairs, identifiers);
    }

    public static DsoFile Load(string path) => Parse(File.ReadAllBytes(path), path);

    private static double[] ReadFloatTable(ref SpanReader r)
    {
        var count = r.ReadUInt32();
        var table = new double[count];
        for (var i = 0u; i < count; i++) table[i] = r.ReadDouble();
        return table;
    }
}

public sealed class DsoFormatException(string? origin, string message)
    : Exception(origin is null ? message : $"{origin}: {message}");
