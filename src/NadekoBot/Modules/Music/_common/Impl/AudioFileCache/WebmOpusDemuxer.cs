using System.Buffers;

namespace NadekoBot.Modules.Music;

// Reads the Opus packets of a WebM file front to back, so it works on a network stream which cannot seek.
public sealed class WebmOpusDemuxer : IDisposable
{
    private const uint EBML_HEADER = 0x1A45DFA3;
    private const uint SEGMENT = 0x18538067;
    private const uint CLUSTER = 0x1F43B675;
    private const uint SIMPLE_BLOCK = 0xA3;
    private const uint TRACKS = 0x1654AE6B;
    private const uint TRACK_ENTRY = 0xAE;
    private const uint TRACK_NUMBER = 0xD7;
    private const uint CODEC_ID = 0x86;
    private const uint BLOCK_GROUP = 0xA0;
    private const uint BLOCK = 0xA1;

    private const long UNKNOWN_SIZE = long.MaxValue;
    private const int MAX_OPUS_PACKET_SIZE = 61_440;

    // Track number (1 or 2 bytes here), relative timecode (2) and flags (1).
    private const int MIN_BLOCK_HEADER_SIZE = 4;
    private const int MAX_BLOCK_HEADER_SIZE = 5;
    private const int MAX_BLOCK_SIZE = MAX_OPUS_PACKET_SIZE + MAX_BLOCK_HEADER_SIZE;
    private const int MAX_ELEMENT_HEADER_SIZE = 12;
    private const int MAX_CODEC_ID_SIZE = 32;
    private const int MAX_UINT_SIZE = 8;
    private const byte LACING_FLAGS = 0x06;
    private const int BUFFER_SIZE = 1 << 16;

    private readonly Stream _stream;
    private byte[]? _buffer;
    private int _start;
    private int _end;
    private bool _eof;
    private long _position;
    private ulong _opusTrack;
    private int _packetStart;
    private int _packetLength;

    public bool IsOpus { get; private set; }

    // Valid until the next read.
    public ReadOnlyMemory<byte> Packet
        => Bytes.AsMemory(_packetStart, _packetLength);

    private byte[] Bytes
        => _buffer ?? throw new ObjectDisposedException(nameof(WebmOpusDemuxer));

    private int Available
        => _end - _start;

    private readonly record struct ElementHeader(uint Id, long Size);

    public WebmOpusDemuxer(Stream stream)
    {
        _stream = stream;
        _buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
    }

    public static FileStream OpenFile(string filePath)
        => new(filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    // Reads the headers up to the first cluster. False when the stream is not WebM with an Opus track.
    public async ValueTask<bool> InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var header = await ReadElementHeaderAsync(ct);
            if (header.Id != EBML_HEADER)
                return false;

            await SkipAsync(header.Size, ct);

            if ((await ReadElementHeaderAsync(ct)).Id != SEGMENT)
                return false;

            while (true)
            {
                header = await ReadElementHeaderAsync(ct);
                switch (header.Id)
                {
                    case 0:
                        return false;
                    case TRACKS:
                        await ParseTracksAsync(header.Size, ct);
                        if (!IsOpus)
                            return false;
                        break;
                    case CLUSTER:
                        return IsOpus;
                    default:
                        await SkipAsync(header.Size, ct);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
        {
            Log.Debug(ex, "The stream is not a WebM file with Opus audio");
            return false;
        }
    }

    // Returns the length of the next packet, or 0 at the end of the stream.
    public async ValueTask<int> ReadPacketAsync(CancellationToken ct = default)
    {
        _packetLength = 0;
        while (true)
        {
            var header = await ReadElementHeaderAsync(ct);
            switch (header.Id)
            {
                case 0:
                    return 0;
                case CLUSTER:
                case BLOCK_GROUP:
                    continue;
                case SIMPLE_BLOCK:
                case BLOCK:
                    if (await ReadBlockAsync(header.Size, ct))
                        return _packetLength;
                    continue;
                default:
                    await SkipAsync(header.Size, ct);
                    continue;
            }
        }
    }

    private async ValueTask<bool> ReadBlockAsync(long size, CancellationToken ct)
    {
        if (size is < MIN_BLOCK_HEADER_SIZE or > MAX_BLOCK_SIZE)
        {
            await SkipAsync(size, ct);
            return false;
        }

        var length = (int)size;
        if (!await FillAsync(length, ct))
            throw new EndOfStreamException();

        var block = Bytes.AsSpan(_start, length);
        int trackHeaderSize;
        ulong track;
        if ((block[0] & 0x80) != 0)
        {
            trackHeaderSize = 1;
            track = (ulong)(block[0] & 0x7F);
        }
        else if ((block[0] & 0x40) != 0)
        {
            trackHeaderSize = 2;
            track = (ulong)(((block[0] & 0x3F) << 8) | block[1]);
        }
        else
        {
            Consume(length);
            return false;
        }

        var headerSize = trackHeaderSize + 3;
        if (length <= headerSize)
        {
            Consume(length);
            return false;
        }

        var flags = block[headerSize - 1];
        var start = _start + headerSize;
        Consume(length);

        if (track != _opusTrack || (flags & LACING_FLAGS) != 0)
            return false;

        _packetStart = start;
        _packetLength = length - headerSize;
        return true;
    }

    private async ValueTask ParseTracksAsync(long size, CancellationToken ct)
    {
        var end = CheckedEnd(size);
        while (_position < end)
        {
            var header = await ReadElementHeaderAsync(ct);
            if (header.Id == TRACK_ENTRY)
                await ParseTrackEntryAsync(header.Size, ct);
            else
                await SkipAsync(header.Size, ct);
        }
    }

    private async ValueTask ParseTrackEntryAsync(long size, CancellationToken ct)
    {
        var end = CheckedEnd(size);
        ulong number = 0;
        var isOpus = false;
        while (_position < end)
        {
            var header = await ReadElementHeaderAsync(ct);
            if (header.Id == TRACK_NUMBER)
                number = await ReadUintAsync(header.Size, ct);
            else if (header.Id == CODEC_ID)
                isOpus = await ReadCodecIsOpusAsync(header.Size, ct);
            else
                await SkipAsync(header.Size, ct);
        }

        if (isOpus && !IsOpus)
        {
            IsOpus = true;
            _opusTrack = number;
        }
    }

    private long CheckedEnd(long size)
        => size == UNKNOWN_SIZE ? throw new InvalidDataException("A WebM header element has no size") : _position + size;

    private async ValueTask<ulong> ReadUintAsync(long size, CancellationToken ct)
    {
        if (size is < 0 or > MAX_UINT_SIZE)
            throw new InvalidDataException("A WebM number is too long");

        if (!await FillAsync((int)size, ct))
            throw new EndOfStreamException();

        ulong value = 0;
        for (var i = 0; i < size; i++)
            value = (value << 8) | ReadByte();

        return value;
    }

    private async ValueTask<bool> ReadCodecIsOpusAsync(long size, CancellationToken ct)
    {
        if (size is <= 0 or > MAX_CODEC_ID_SIZE)
        {
            await SkipAsync(size, ct);
            return false;
        }

        var length = (int)size;
        if (!await FillAsync(length, ct))
            throw new EndOfStreamException();

        var isOpus = Bytes.AsSpan(_start, length).SequenceEqual("A_OPUS"u8);
        Consume(length);
        return isOpus;
    }

    // Id 0 means the stream ended at an element boundary.
    private async ValueTask<ElementHeader> ReadElementHeaderAsync(CancellationToken ct)
    {
        await FillAsync(MAX_ELEMENT_HEADER_SIZE, ct);
        if (Available == 0)
            return default;

        var id = ReadVintId();
        return new(id, ReadVintSize());
    }

    private uint ReadVintId()
    {
        var first = ReadByte();
        var length = first switch
        {
            >= 0x80 => 1,
            >= 0x40 => 2,
            >= 0x20 => 3,
            >= 0x10 => 4,
            _ => throw new InvalidDataException($"Invalid EBML element id byte 0x{first:X2}")
        };

        uint id = first;
        for (var i = 1; i < length; i++)
            id = (id << 8) | ReadByte();

        return id;
    }

    private long ReadVintSize()
    {
        var first = ReadByte();
        if (first == 0)
            throw new InvalidDataException("Invalid EBML size byte 0x00");

        var length = System.Numerics.BitOperations.LeadingZeroCount((uint)first) - 23;
        long value = first & ((0x80 >> (length - 1)) - 1);
        for (var i = 1; i < length; i++)
            value = (value << 8) | ReadByte();

        return value == (1L << (7 * length)) - 1 ? UNKNOWN_SIZE : value;
    }

    private byte ReadByte()
    {
        if (_start == _end)
            throw new EndOfStreamException();

        _position++;
        return Bytes[_start++];
    }

    private void Consume(int count)
    {
        _start += count;
        _position += count;
    }

    private async ValueTask SkipAsync(long count, CancellationToken ct)
    {
        if (count == UNKNOWN_SIZE)
            throw new InvalidDataException("Cannot skip a WebM element without a size");

        while (count > 0)
        {
            if (Available == 0 && !await FillAsync(1, ct))
                throw new EndOfStreamException();

            var n = (int)Math.Min(count, Available);
            Consume(n);
            count -= n;
        }
    }

    // False when the stream ends before count bytes.
    private async ValueTask<bool> FillAsync(int count, CancellationToken ct)
    {
        if (Available >= count)
            return true;

        var buffer = Bytes;
        if (_start > 0)
        {
            Buffer.BlockCopy(buffer, _start, buffer, 0, Available);
            _end -= _start;
            _start = 0;
        }

        while (!_eof && _end < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(_end, BUFFER_SIZE - _end), ct);
            if (read == 0)
                _eof = true;
            else
                _end += read;
        }

        return _end >= count;
    }

    public void Dispose()
    {
        _stream.Dispose();
        if (Interlocked.Exchange(ref _buffer, null) is { } buffer)
            ArrayPool<byte>.Shared.Return(buffer);
    }
}
