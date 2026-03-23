namespace NadekoBot.Modules.Music;

public sealed class WebmOpusDemuxer : IDisposable
{
    private const uint EBML_HEADER = 0x1A45DFA3;
    private const uint SEGMENT = 0x18538067;
    private const uint CLUSTER = 0x1F43B675;
    private const uint SIMPLE_BLOCK = 0xA3;
    private const uint TIMECODE = 0xE7;
    private const uint TRACKS = 0x1654AE6B;
    private const uint TRACK_ENTRY = 0xAE;
    private const uint CODEC_ID = 0x86;
    private const uint SEGMENT_INFO = 0x1549A966;
    private const uint SEEK_HEAD = 0x114D9B74;
    private const uint CUES = 0x1C53BB6B;
    private const uint TAGS = 0x1254C367;
    private const uint BLOCK_GROUP = 0xA0;
    private const uint BLOCK = 0xA1;
    private const uint VOID = 0xEC;
    private const uint TIMESTAMP_SCALE = 0x2AD7B1;

    private const int MAX_OPUS_PACKET_SIZE = 61_440;
    private static readonly TimeSpan MAX_WAIT_TIMEOUT = TimeSpan.FromSeconds(10);

    private readonly FileStream _stream;
    private readonly byte[] _readBuffer;
    private readonly CacheFileState? _state;
    private bool _headerParsed;
    private bool _isOpus;

    public bool IsOpus => _isOpus;

    public WebmOpusDemuxer(string filePath, CacheFileState? state = null)
    {
        _state = state;

        if (state is not null && !File.Exists(filePath))
        {
            while (!state.IsDone && !File.Exists(filePath))
            {
                var ver = state.Version;
                state.WaitForData(ver, MAX_WAIT_TIMEOUT);
            }

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Cache file was never created", filePath);
        }

        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 81_920, useAsync: false);
        _readBuffer = new byte[MAX_OPUS_PACKET_SIZE];
    }

    public bool Initialize()
    {
        if (_headerParsed)
            return _isOpus;

        _headerParsed = true;

        try
        {
            var (headerId, headerSize) = ReadElementHeader();
            if (headerId != EBML_HEADER)
                return false;

            Skip(headerSize);

            var (segId, _) = ReadElementHeader();
            if (segId != SEGMENT)
                return false;

            ScanSegmentHeaders();
            return _isOpus;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse WebM header");
            return false;
        }
    }

    public bool TryReadPacket(out byte[] data, out int length)
    {
        data = _readBuffer;
        length = 0;

        try
        {
            while (true)
            {
                if (!HasDataAvailable(1))
                    return false;

                var (elementId, elementSize) = ReadElementHeader();

                switch (elementId)
                {
                    case CLUSTER:
                    case BLOCK_GROUP:
                        continue;

                    case TIMECODE:
                        Skip(elementSize);
                        continue;

                    case SIMPLE_BLOCK:
                    case BLOCK:
                        return ReadSimpleBlockOpusData(elementSize, out data, out length);

                    case CUES:
                    case TAGS:
                    case VOID:
                        Skip(elementSize);
                        continue;

                    default:
                        if (elementSize > 0 && elementSize < GetKnownDataSize() - _stream.Position)
                            Skip(elementSize);
                        else
                            return false;
                        continue;
                }
            }
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error reading WebM Opus packet at position {Position}", _stream.Position);
            return false;
        }
    }

    private bool ReadSimpleBlockOpusData(long blockSize, out byte[] data, out int length)
    {
        data = _readBuffer;
        length = 0;

        var trackByte = ReadByte();
        int trackHeaderSize;
        if ((trackByte & 0x80) != 0)
            trackHeaderSize = 1;
        else if ((trackByte & 0x40) != 0)
        {
            ReadByte();
            trackHeaderSize = 2;
        }
        else
        {
            Skip(blockSize - 1);
            return false;
        }

        // skip relative timecode (2 bytes) and flags (1 byte)
        Skip(3);

        var opusDataSize = (int)(blockSize - trackHeaderSize - 3);

        if (opusDataSize <= 0 || opusDataSize > MAX_OPUS_PACKET_SIZE)
        {
            if (opusDataSize > 0)
                Skip(opusDataSize);
            return false;
        }

        ReadExact(data, opusDataSize);
        length = opusDataSize;
        return true;
    }

    private void ScanSegmentHeaders()
    {
        while (HasDataAvailable(1))
        {
            var pos = _stream.Position;
            var (elementId, elementSize) = ReadElementHeader();

            switch (elementId)
            {
                case SEGMENT_INFO:
                    ParseSegmentInfo(elementSize);
                    break;

                case TRACKS:
                    ParseTracks(elementSize);
                    break;

                case CLUSTER:
                    _stream.Position = pos;
                    return;

                case SEEK_HEAD:
                case CUES:
                case TAGS:
                case VOID:
                    Skip(elementSize);
                    break;

                default:
                    if (elementSize > 0 && elementSize < GetKnownDataSize() - _stream.Position)
                        Skip(elementSize);
                    else
                        return;
                    break;
            }
        }
    }

    private void ParseSegmentInfo(long size)
    {
        var end = _stream.Position + size;
        while (_stream.Position < end)
        {
            var (id, sz) = ReadElementHeader();
            if (id == TIMESTAMP_SCALE)
                ReadUint(sz);
            else
                Skip(sz);
        }
    }

    private void ParseTracks(long size)
    {
        var end = _stream.Position + size;
        while (_stream.Position < end)
        {
            var (id, sz) = ReadElementHeader();
            if (id == TRACK_ENTRY)
                ParseTrackEntry(sz);
            else
                Skip(sz);
        }
    }

    private void ParseTrackEntry(long size)
    {
        var end = _stream.Position + size;
        while (_stream.Position < end)
        {
            var (id, sz) = ReadElementHeader();
            if (id == CODEC_ID)
            {
                var codecBytes = new byte[sz];
                ReadExact(codecBytes, (int)sz);
                var codecStr = System.Text.Encoding.ASCII.GetString(codecBytes);
                if (codecStr == "A_OPUS")
                    _isOpus = true;
            }
            else
            {
                Skip(sz);
            }
        }
    }

    private bool HasDataAvailable(int needed)
        => WaitForData(needed);

    private (uint Id, long Size) ReadElementHeader()
    {
        var id = ReadVintId();
        var size = ReadVintSize();
        return (id, size);
    }

    private uint ReadVintId()
    {
        var first = ReadByte();

        if ((first & 0x80) != 0)
            return first;

        if ((first & 0x40) != 0)
            return (uint)((first << 8) | ReadByte());

        if ((first & 0x20) != 0)
        {
            var b1 = ReadByte();
            var b2 = ReadByte();
            return (uint)((first << 16) | (b1 << 8) | b2);
        }

        if ((first & 0x10) != 0)
        {
            var b1 = ReadByte();
            var b2 = ReadByte();
            var b3 = ReadByte();
            return (uint)((first << 24) | (b1 << 16) | (b2 << 8) | b3);
        }

        throw new InvalidDataException($"Invalid EBML element ID leading byte: 0x{first:X2}");
    }

    private long ReadVintSize()
    {
        var first = ReadByte();
        int length;
        long value;

        if ((first & 0x80) != 0) { length = 1; value = first & 0x7F; }
        else if ((first & 0x40) != 0) { length = 2; value = first & 0x3F; }
        else if ((first & 0x20) != 0) { length = 3; value = first & 0x1F; }
        else if ((first & 0x10) != 0) { length = 4; value = first & 0x0F; }
        else if ((first & 0x08) != 0) { length = 5; value = first & 0x07; }
        else if ((first & 0x04) != 0) { length = 6; value = first & 0x03; }
        else if ((first & 0x02) != 0) { length = 7; value = first & 0x01; }
        else { length = 8; value = 0; }

        for (var i = 1; i < length; i++)
            value = (value << 8) | ReadByte();

        var allOnes = (1L << (7 * length)) - 1;
        if (value == allOnes)
            return long.MaxValue;

        return value;
    }

    private ulong ReadUint(long size)
    {
        ulong value = 0;
        for (var i = 0; i < size; i++)
            value = (value << 8) | ReadByte();
        return value;
    }

    private byte ReadByte()
    {
        if (!WaitForData(1))
            throw new EndOfStreamException();

        var b = _stream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException();
        return (byte)b;
    }

    private void ReadExact(byte[] buffer, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            if (!WaitForData(1))
                throw new EndOfStreamException();

            var read = _stream.Read(buffer, totalRead, count - totalRead);
            if (read == 0)
            {
                if (!WaitForData(1))
                    throw new EndOfStreamException();
                continue;
            }

            totalRead += read;
        }
    }

    private void Skip(long count)
    {
        if (count <= 0)
            return;

        if (!WaitForData(count))
            throw new EndOfStreamException();

        _stream.Position += count;
    }

    private long GetKnownDataSize()
        => _state?.BytesWritten ?? _stream.Length;

    private bool WaitForData(long needed)
    {
        var targetPosition = _stream.Position + needed;

        if (_state is not null)
        {
            if (_state.BytesWritten >= targetPosition)
                return true;
        }
        else
        {
            return _stream.Position + needed <= _stream.Length;
        }

        if (_state.IsDone)
            return _state.BytesWritten >= targetPosition;

        while (!_state.IsDone)
        {
            var ver = _state.Version;
            if (_state.BytesWritten >= targetPosition)
                return true;
            _state.WaitForData(ver, MAX_WAIT_TIMEOUT);
        }

        return _state.BytesWritten >= targetPosition;
    }

    public void Dispose()
        => _stream.Dispose();
}
