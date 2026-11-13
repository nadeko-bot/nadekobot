using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace NadekoBot.Modules.Searches.Charts;

// Writes an 8-bit RGB PNG. Each row uses the Sub or the Up filter, whichever gives the smaller sum.
public static class PngWriter
{
    private const int BYTES_PER_PIXEL = 3;
    private const byte FILTER_SUB = 1;
    private const byte FILTER_UP = 2;
    private const byte COLOR_TYPE_RGB = 2;
    private const int ROW_SLACK = 4;
    private const int MAX_STEPS = 255;

    private static readonly uint[] _crcTable = CreateCrcTableInternal();

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(ReadOnlySpan<uint> pixels, int width, int height, CompressionLevel level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height);

        var rowBytes = width * BYTES_PER_PIXEL;
        var rowStride = rowBytes + ROW_SLACK;
        var rows = ArrayPool<byte>.Shared.Rent((rowStride * 2) + ((rowBytes + 1) * 2));
        var output = new PooledBufferStream(64 * 1024);
        try
        {
            output.Write(Signature);

            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
            header[8] = 8;
            header[9] = COLOR_TYPE_RGB;
            header[10..].Clear();
            WriteChunkInternal(output, "IHDR"u8, header);

            var idatStart = output.Count;
            Span<byte> placeholder = stackalloc byte[4];
            placeholder.Clear();
            output.Write(placeholder);
            output.Write("IDAT"u8);

            using (var zlib = new ZLibStream(output, level, leaveOpen: true))
            {
                var previous = rows.AsSpan(0, rowStride);
                var current = rows.AsSpan(rowStride, rowStride);
                var sub = rows.AsSpan(rowStride * 2, rowBytes + 1);
                var up = rows.AsSpan((rowStride * 2) + rowBytes + 1, rowBytes + 1);
                previous.Clear();
                sub[0] = FILTER_SUB;
                up[0] = FILTER_UP;

                for (var y = 0; y < height; y++)
                {
                    ToRgbInternal(pixels.Slice(y * width, width), current);
                    var (subSum, upSum) = FilterInternal(current[..rowBytes], previous[..rowBytes], sub[1..], up[1..]);
                    zlib.Write(subSum <= upSum ? sub : up);

                    var swap = previous;
                    previous = current;
                    current = swap;
                }
            }

            var written = output.Written;
            var dataLength = written.Length - idatStart - 8;
            BinaryPrimitives.WriteInt32BigEndian(written[idatStart..], dataLength);
            var crc = Crc32(written.Slice(idatStart + 4, dataLength + 4));
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
            output.Write(crcBytes);

            WriteChunkInternal(output, "IEND"u8, []);
            return output.Written.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rows);
            output.Dispose();
        }
    }

    // 0x00RRGGBB in memory is B, G, R, 0. Each step reads 4 pixels and writes 16 bytes, of which the next step overwrites the last 4.
    private static void ToRgbInternal(ReadOnlySpan<uint> source, Span<byte> row)
    {
        var bytes = MemoryMarshal.AsBytes(source);
        var x = 0;
        var o = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var order = Vector128.Create((byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 15, 15, 15, 15);
            for (; x + 4 <= source.Length && o + Vector128<byte>.Count <= row.Length; x += 4, o += 4 * BYTES_PER_PIXEL)
                Vector128.Shuffle(Vector128.Create(bytes.Slice(x * 4, Vector128<byte>.Count)), order).CopyTo(row[o..]);
        }

        for (; x < source.Length; x++, o += BYTES_PER_PIXEL)
        {
            var px = source[x];
            row[o] = (byte)(px >> 16);
            row[o + 1] = (byte)(px >> 8);
            row[o + 2] = (byte)px;
        }
    }

    // Writes both filtered rows and returns the sum of the absolute signed bytes of each.
    private static (int Sub, int Up) FilterInternal(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, Span<byte> sub, Span<byte> up)
    {
        var subSum = 0;
        var upSum = 0;
        var i = 0;
        for (; i < BYTES_PER_PIXEL && i < current.Length; i++)
        {
            sub[i] = current[i];
            up[i] = (byte)(current[i] - previous[i]);
            subSum += Math.Abs((int)(sbyte)sub[i]);
            upSum += Math.Abs((int)(sbyte)up[i]);
        }

        if (Vector.IsHardwareAccelerated)
        {
            var subAcc = Vector<ushort>.Zero;
            var upAcc = Vector<ushort>.Zero;
            var steps = 0;
            for (; i + Vector<byte>.Count <= current.Length; i += Vector<byte>.Count)
            {
                var c = new Vector<byte>(current[i..]);
                var s = c - new Vector<byte>(current[(i - BYTES_PER_PIXEL)..]);
                var u = c - new Vector<byte>(previous[i..]);
                s.CopyTo(sub[i..]);
                u.CopyTo(up[i..]);

                // The absolute value of a signed byte is min(b, 256 - b) of its unsigned byte b, which also holds for -128.
                Vector.Widen(Vector.Min(s, Vector<byte>.Zero - s), out var subLow, out var subHigh);
                Vector.Widen(Vector.Min(u, Vector<byte>.Zero - u), out var upLow, out var upHigh);
                subAcc += subLow + subHigh;
                upAcc += upLow + upHigh;

                // A lane grows by at most 256 in each step, so it is emptied before it can pass 65535.
                if (++steps == MAX_STEPS)
                {
                    subSum += SumInternal(subAcc);
                    upSum += SumInternal(upAcc);
                    subAcc = Vector<ushort>.Zero;
                    upAcc = Vector<ushort>.Zero;
                    steps = 0;
                }
            }

            subSum += SumInternal(subAcc);
            upSum += SumInternal(upAcc);
        }

        for (; i < current.Length; i++)
        {
            var s = (byte)(current[i] - current[i - BYTES_PER_PIXEL]);
            var u = (byte)(current[i] - previous[i]);
            sub[i] = s;
            up[i] = u;
            subSum += Math.Abs((int)(sbyte)s);
            upSum += Math.Abs((int)(sbyte)u);
        }

        return (subSum, upSum);
    }

    private static int SumInternal(Vector<ushort> value)
    {
        Vector.Widen(value, out var low, out var high);
        return (int)Vector.Sum(low + high);
    }

    private static void WriteChunkInternal(PooledBufferStream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);

        var start = output.Count;
        output.Write(type);
        output.Write(data);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(output.Written.Slice(start, type.Length + data.Length)));
        output.Write(number);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = _crcTable[(int)((crc ^ b) & 0xFF)] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] CreateCrcTableInternal()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }

    private sealed class PooledBufferStream(int capacity) : Stream
    {
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        private int _length;

        public Span<byte> Written
            => _buffer.AsSpan(0, _length);

        public override bool CanRead
            => false;

        public override bool CanSeek
            => false;

        public override bool CanWrite
            => true;

        public override long Length
            => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public int Count
            => _length;

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            if (_length + data.Length > _buffer.Length)
            {
                var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_length + data.Length, _buffer.Length * 2));
                _buffer.AsSpan(0, _length).CopyTo(bigger);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = bigger;
            }

            data.CopyTo(_buffer.AsSpan(_length));
            _length += data.Length;
        }

        public override void WriteByte(byte value)
            => Write([value]);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = [];
            }

            base.Dispose(disposing);
        }
    }
}
