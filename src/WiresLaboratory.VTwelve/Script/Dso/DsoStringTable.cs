using System.Text;

namespace WiresLaboratory.VTwelve.Script.Dso;

/// <summary>
/// A DSO string table: one blob of NUL-separated text, indexed by <em>byte offset</em>
/// rather than by ordinal, because the bytecode stores raw offsets into it.
/// </summary>
public sealed class DsoStringTable
{
    private readonly byte[] _raw;

    private DsoStringTable(byte[] raw) => _raw = raw;

    /// <summary>Size of the table in bytes (the addressable range for offsets).</summary>
    public int ByteLength => _raw.Length;

    internal static DsoStringTable Read(ref SpanReader r)
    {
        var size = r.ReadUInt32();
        return new DsoStringTable(r.ReadBytes((int)size).ToArray());
    }

    /// <summary>
    /// Reads the NUL-terminated string starting at <paramref name="offset"/>. The engine
    /// addresses mid-table offsets freely, so this is a scan from the offset, not a lookup.
    /// </summary>
    public string GetString(uint offset)
    {
        if (offset >= (uint)_raw.Length) return string.Empty;
        var start = (int)offset;
        var end = Array.IndexOf(_raw, (byte)0, start);
        if (end < 0) end = _raw.Length;
        // V12 predates UTF-8 in this role; the tables are single-byte extended ASCII.
        return Encoding.Latin1.GetString(_raw, start, end - start);
    }

    /// <summary>Enumerates every distinct string, paired with its byte offset.</summary>
    public IEnumerable<(uint Offset, string Value)> Enumerate()
    {
        var i = 0;
        while (i < _raw.Length)
        {
            var end = Array.IndexOf(_raw, (byte)0, i);
            if (end < 0) end = _raw.Length;
            if (end > i) yield return ((uint)i, Encoding.Latin1.GetString(_raw, i, end - i));
            i = end + 1;
        }
    }
}
