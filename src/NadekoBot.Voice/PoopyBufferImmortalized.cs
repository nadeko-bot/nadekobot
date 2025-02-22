#nullable enable
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace NadekoBot.Voice
{
    public sealed class PoopyBufferImmortalized : ISongBuffer
    {
        private readonly byte[] _buffer;
        private readonly byte[] _outputArray;
        private CancellationToken _cancellationToken;
        private bool _isStopped;

        public int ReadPosition { get; private set; }
        public int WritePosition { get; private set; }

        public int ContentLength => WritePosition >= ReadPosition
            ? WritePosition - ReadPosition
            : (_buffer.Length - ReadPosition) + WritePosition;

        public int FreeSpace => _buffer.Length - ContentLength;

        public bool Stopped => _cancellationToken.IsCancellationRequested || _isStopped;

        public PoopyBufferImmortalized(int frameSize)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(1_000_000);
            _outputArray = new byte[frameSize];

            ReadPosition = 0;
            WritePosition = 0;
        }

        public void Stop()
            => _isStopped = true;

        // this method needs a rewrite
        public Task<bool> BufferAsync(ITrackDataSource source, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            var bufferingCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.Run(async () =>
            {
                var output = ArrayPool<byte>.Shared.Rent(38400);
                try
                {
                    int read;
                    while (!Stopped && (read = source.Read(output)) > 0)
                    {
                        while (!Stopped && FreeSpace <= read)
                        {
                            bufferingCompleted.TrySetResult(true);
                            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        }

                        if (Stopped)
                            break;

                        Write(output, read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(output);
                    bufferingCompleted.TrySetResult(true);
                }
            }, cancellationToken);

            return bufferingCompleted.Task;
        }

        private void Write(byte[] input, int writeCount)
        {
            if (WritePosition + writeCount < _buffer.Length)
            {
                Buffer.BlockCopy(input, 0, _buffer, WritePosition, writeCount);
                WritePosition += writeCount;
                return;
            }

            var wroteNormally = _buffer.Length - WritePosition;
            Buffer.BlockCopy(input, 0, _buffer, WritePosition, wroteNormally);
            var wroteFromStart = writeCount - wroteNormally;
            Buffer.BlockCopy(input, wroteNormally, _buffer, 0, wroteFromStart);
            WritePosition = wroteFromStart;
        }

        public Span<byte> Read(int count, out int length)
        {
            var toRead = Math.Min(ContentLength, count);
            var wp = WritePosition;

            if (ContentLength == 0)
            {
                length = 0;
                return Span<byte>.Empty;
            }

            if (wp > ReadPosition || ReadPosition + toRead <= _buffer.Length)
            {
                // thsi can be achieved without copying if 
                // writer never writes until the end,
                // but leaves a single chunk free
                Span<byte> toReturn = _outputArray;
                ((Span<byte>) _buffer).Slice(ReadPosition, toRead).CopyTo(toReturn);
                ReadPosition += toRead;
                length = toRead;
                return toReturn;
            }
            else
            {
                Span<byte> toReturn = _outputArray;
                var toEnd = _buffer.Length - ReadPosition;
                var bufferSpan = (Span<byte>) _buffer;

                bufferSpan.Slice(ReadPosition, toEnd).CopyTo(toReturn);
                var fromStart = toRead - toEnd;
                bufferSpan.Slice(0, fromStart).CopyTo(toReturn.Slice(toEnd));
                ReadPosition = fromStart;
                length = toEnd + fromStart;
                return toReturn;
            }
        }

        public void Dispose()
            => ArrayPool<byte>.Shared.Return(_buffer);

        public void Reset()
        {
            ReadPosition = 0;
            WritePosition = 0;
        }
    }
}