using System.Buffers;
using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 1: DAVE audioPayload copy elimination.
/// Simulates the rent->encrypt->copy->return->use pattern vs rent->encrypt->use->return.
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class DaveAudioPayloadBenchmarks
{
    private byte[] _inputData = null!;
    private byte[] _secretKey = null!;
    private byte[] _rtpHeader = null!;
    private byte[] _nonce = null!;
    private const int FrameSize = 960; // typical opus frame
    private const int DaveOverhead = 64;
    private const int SodiumTagSize = 16;
    private const int RtpHeaderLength = 12;
    private const int NonceSuffixSize = 4;

    [GlobalSetup]
    public void Setup()
    {
        _inputData = new byte[FrameSize];
        Random.Shared.NextBytes(_inputData);
        _secretKey = new byte[32];
        Random.Shared.NextBytes(_secretKey);
        _rtpHeader = new byte[12];
        _nonce = new byte[24];
    }

    [Benchmark(Description = "Current: Rent+Copy+Return")]
    public int Current_RentCopyReturn()
    {
        var pool = ArrayPool<byte>.Shared;

        // simulate DAVE encrypt into rented buffer
        var maxEncSize = FrameSize + DaveOverhead;
        var encryptedFrame = pool.Rent(maxEncSize);
        byte[] audioPayload;
        int audioPayloadLength;
        try
        {
            // simulate DAVE encrypt
            Buffer.BlockCopy(_inputData, 0, encryptedFrame, 0, FrameSize);
            var encLen = FrameSize + 17; // simulated DAVE ciphertext size

            // THIS IS THE ALLOCATION WE WANT TO ELIMINATE
            audioPayload = new byte[encLen];
            Buffer.BlockCopy(encryptedFrame, 0, audioPayload, 0, encLen);
            audioPayloadLength = encLen;
        }
        finally
        {
            pool.Return(encryptedFrame);
        }

        // simulate Sodium encrypt
        var encryptedLength = audioPayloadLength + SodiumTagSize;
        var rtpDataLength = RtpHeaderLength + encryptedLength + NonceSuffixSize;
        var rtpData = pool.Rent(rtpDataLength);
        try
        {
            // simulate Sodium.Encrypt reading from audioPayload
            Buffer.BlockCopy(audioPayload, 0, rtpData, RtpHeaderLength, audioPayloadLength);
            Buffer.BlockCopy(_rtpHeader, 0, rtpData, 0, RtpHeaderLength);
            Buffer.BlockCopy(_nonce, 0, rtpData, RtpHeaderLength + encryptedLength, NonceSuffixSize);
            return rtpDataLength;
        }
        finally
        {
            pool.Return(rtpData);
        }
    }

    [Benchmark(Description = "Fixed: Rent+Use+Return")]
    public int Fixed_RentUseReturn()
    {
        var pool = ArrayPool<byte>.Shared;

        var maxEncSize = FrameSize + DaveOverhead;
        var encryptedFrame = pool.Rent(maxEncSize);
        try
        {
            // simulate DAVE encrypt
            Buffer.BlockCopy(_inputData, 0, encryptedFrame, 0, FrameSize);
            var encLen = FrameSize + 17;

            // NO COPY - use encryptedFrame directly

            // simulate Sodium encrypt
            var encryptedLength = encLen + SodiumTagSize;
            var rtpDataLength = RtpHeaderLength + encryptedLength + NonceSuffixSize;
            var rtpData = pool.Rent(rtpDataLength);
            try
            {
                // Sodium.Encrypt reads directly from encryptedFrame
                Buffer.BlockCopy(encryptedFrame, 0, rtpData, RtpHeaderLength, encLen);
                Buffer.BlockCopy(_rtpHeader, 0, rtpData, 0, RtpHeaderLength);
                Buffer.BlockCopy(_nonce, 0, rtpData, RtpHeaderLength + encryptedLength, NonceSuffixSize);
                return rtpDataLength;
            }
            finally
            {
                pool.Return(rtpData);
            }
        }
        finally
        {
            pool.Return(encryptedFrame);
        }
    }
}
