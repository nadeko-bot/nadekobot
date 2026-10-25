using System.Buffers;
using System.Buffers.Binary;

namespace NadekoBot.Modules.Music;

// A single-producer single-consumer queue of Opus packets in one pooled array. Each entry is a 2 byte length and the
// packet. A length of WRAP, or less than 2 bytes left before the end, means the next entry starts at the beginning.
public sealed class OpusPacketRing : IDisposable
{
    private const ushort WRAP = ushort.MaxValue;
    private const int LENGTH_SIZE = sizeof(ushort);

    public const int MAX_PACKET_SIZE = WRAP - 1;

    private readonly int _capacity;
    private readonly int _mask;
    private byte[]? _buffer;
    private long _written;
    private long _read;
    private int _count;

    public int Count
        => Volatile.Read(ref _count);

    public OpusPacketRing(int capacityPowerOfTwo)
    {
        if (capacityPowerOfTwo <= MAX_PACKET_SIZE + LENGTH_SIZE || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacityPowerOfTwo));

        _capacity = capacityPowerOfTwo;
        _mask = capacityPowerOfTwo - 1;
        _buffer = ArrayPool<byte>.Shared.Rent(capacityPowerOfTwo);
    }

    private byte[] Bytes
        => _buffer ?? throw new ObjectDisposedException(nameof(OpusPacketRing));

    // False when the ring is full.
    public bool TryWrite(ReadOnlySpan<byte> packet)
    {
        if (packet.Length is 0 or > MAX_PACKET_SIZE)
            throw new ArgumentOutOfRangeException(nameof(packet));

        var bytes = Bytes;
        var written = _written;
        var index = (int)(written & _mask);
        var need = LENGTH_SIZE + packet.Length;
        var padding = _capacity - index < need ? _capacity - index : 0;

        if (_capacity - (written - Volatile.Read(ref _read)) < padding + need)
            return false;

        if (padding > 0)
        {
            if (padding >= LENGTH_SIZE)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index), WRAP);

            index = 0;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index), (ushort)packet.Length);
        packet.CopyTo(bytes.AsSpan(index + LENGTH_SIZE));

        Volatile.Write(ref _written, written + padding + need);
        Interlocked.Increment(ref _count);
        return true;
    }

    // Copies the oldest packet into output. Returns its length, or 0 when the ring is empty.
    public int TryRead(Span<byte> output)
    {
        var bytes = Bytes;
        var read = _read;
        if (read == Volatile.Read(ref _written))
            return 0;

        var index = (int)(read & _mask);
        var left = _capacity - index;
        if (left < LENGTH_SIZE || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index)) == WRAP)
        {
            read += left;
            index = 0;
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index));
        bytes.AsSpan(index + LENGTH_SIZE, length).CopyTo(output);

        Interlocked.Decrement(ref _count);
        Volatile.Write(ref _read, read + LENGTH_SIZE + length);
        return length;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _buffer, null) is { } buffer)
            ArrayPool<byte>.Shared.Return(buffer);
    }
}
