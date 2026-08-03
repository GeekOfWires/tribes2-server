using System.Buffers.Binary;

namespace WiresLaboratory.VTwelve.Script.Dso;

/// <summary>
/// Little-endian forward reader over a byte span, with bounds errors that name the file
/// being parsed. V12 was x86-only, so every scalar in a DSO is little-endian.
/// </summary>
internal ref struct SpanReader(ReadOnlySpan<byte> data, string? origin)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private readonly string? _origin = origin;
    private int _pos = 0;

    public readonly bool AtEnd => _pos >= _data.Length;
    public readonly int Remaining => _data.Length - _pos;

    public byte ReadByte()
    {
        Need(1);
        return _data[_pos++];
    }

    public uint ReadUInt32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
        _pos += 4;
        return v;
    }

    public double ReadDouble()
    {
        Need(8);
        var v = BinaryPrimitives.ReadDoubleLittleEndian(_data[_pos..]);
        _pos += 8;
        return v;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Need(count);
        var s = _data.Slice(_pos, count);
        _pos += count;
        return s;
    }

    private readonly void Need(int count)
    {
        if (count < 0 || _pos + count > _data.Length)
            throw new DsoFormatException(_origin,
                $"unexpected end of file: wanted {count} byte(s) at offset {_pos} of {_data.Length}");
    }
}
