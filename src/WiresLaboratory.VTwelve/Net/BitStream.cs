namespace WiresLaboratory.VTwelve.Net;

/// <summary>
/// Bit-level packer/unpacker used by every V12 network packet: the connection handshake,
/// datablock transfer, ghost updates and client moves.
/// </summary>
/// <remarks>
/// <para>
/// Bit order is the part that must be exactly right or nothing downstream decodes: within a
/// byte, bit 0 is the least-significant bit, and a multi-bit value is written
/// least-significant-bit first. That layout is asserted by round-trip tests and, more
/// importantly, by decoding real captured traffic from the reference server — self-consistency
/// alone would not prove compatibility with the stock client.
/// </para>
/// <para>
/// This is deliberately a single type that both reads and writes, mirroring the original: the
/// same pack/unpack routine is often written once and run in both directions, and keeping one
/// cursor makes that symmetry checkable.
/// </para>
/// </remarks>
public sealed class BitStream
{
    private byte[] _buffer;
    private int _bitPos;
    private readonly int _maxBits;

    /// <summary>Creates a stream for writing into a fresh buffer.</summary>
    public BitStream(int capacityBytes = 1500)
    {
        _buffer = new byte[capacityBytes];
        _maxBits = capacityBytes * 8;
    }

    /// <summary>Creates a stream for reading over existing data (not copied).</summary>
    public BitStream(byte[] data, int length = -1)
    {
        _buffer = data;
        _maxBits = (length < 0 ? data.Length : length) * 8;
    }

    /// <summary>Current cursor position, in bits.</summary>
    public int BitPosition
    {
        get => _bitPos;
        set
        {
            if (value < 0 || value > _maxBits) throw new ArgumentOutOfRangeException(nameof(value));
            _bitPos = value;
        }
    }

    /// <summary>Bytes needed to hold everything written so far (the cursor rounded up).</summary>
    public int ByteLength => (_bitPos + 7) / 8;

    /// <summary>Bits still available before the buffer is full.</summary>
    public int BitsRemaining => _maxBits - _bitPos;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, ByteLength);

    public byte[] ToArray() => WrittenSpan.ToArray();

    /// <summary>Advances the cursor to the next byte boundary.</summary>
    public void AlignToByte()
    {
        var rem = _bitPos & 7;
        if (rem != 0) _bitPos += 8 - rem;
    }

    // ---------------------------------------------------------------- writing

    public void WriteFlag(bool value)
    {
        Need(1);
        var byteIndex = _bitPos >> 3;
        var bit = _bitPos & 7;
        if (value) _buffer[byteIndex] |= (byte)(1 << bit);
        else _buffer[byteIndex] &= (byte)~(1 << bit);
        _bitPos++;
    }

    /// <summary>Writes the low <paramref name="bitCount"/> bits of <paramref name="value"/>.</summary>
    public void WriteInt(uint value, int bitCount)
    {
        if (bitCount is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(bitCount));
        Need(bitCount);
        for (var i = 0; i < bitCount; i++)
            WriteFlag(((value >> i) & 1u) != 0);
    }

    public void WriteSignedInt(int value, int bitCount) => WriteInt(unchecked((uint)value), bitCount);

    /// <summary>Writes raw bytes at the current position (bit-aligned, not byte-aligned).</summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        foreach (var b in data) WriteInt(b, 8);
    }

    // ---------------------------------------------------------------- reading

    public bool ReadFlag()
    {
        Need(1);
        var byteIndex = _bitPos >> 3;
        var bit = _bitPos & 7;
        var v = (_buffer[byteIndex] & (1 << bit)) != 0;
        _bitPos++;
        return v;
    }

    public uint ReadInt(int bitCount)
    {
        if (bitCount is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(bitCount));
        Need(bitCount);
        uint v = 0;
        for (var i = 0; i < bitCount; i++)
            if (ReadFlag()) v |= 1u << i;
        return v;
    }

    public int ReadSignedInt(int bitCount) => unchecked((int)ReadInt(bitCount));

    public void ReadBytes(Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++) destination[i] = (byte)ReadInt(8);
    }

    // ---------------------------------------------------------------- helpers

    private void Need(int bits)
    {
        if (_bitPos + bits > _maxBits)
            throw new InvalidOperationException(
                $"BitStream overrun: need {bits} bit(s) at {_bitPos} of {_maxBits}");
    }
}
