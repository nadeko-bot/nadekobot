using System;
using System.Buffers;
using System.Buffers.Binary;

namespace NadekoBot.Voice
{
    public sealed class VoiceClient : IDisposable
    {
        delegate int EncodeDelegate(Span<byte> input, byte[] output);

        private readonly int sampleRate;
        private readonly int bitRate;
        private readonly int channels;
        private readonly int frameDelay;
        private readonly int bitDepth;

        public LibOpusEncoder Encoder { get; }
        private readonly ArrayPool<byte> _arrayPool;

        // pre-allocated buffers to avoid per-packet heap allocations
        private readonly byte[] _rtpHeader = new byte[RtpHeaderLength];
        private readonly byte[] _nonce = new byte[Sodium.NONCE_SIZE];
        private const int RtpHeaderLength = 12;
        private const int NonceSuffixSize = 4;

        public int BitDepth => bitDepth * 8;
        public int Delay => frameDelay;

        private int FrameSizePerChannel => Encoder.FrameSizePerChannel;
        public int InputLength => FrameSizePerChannel * channels * bitDepth;

        EncodeDelegate Encode;

        public VoiceClient(SampleRate sampleRate = SampleRate._48k,
            Bitrate bitRate = Bitrate._192k,
            Channels channels = Channels.Two,
            FrameDelay frameDelay = FrameDelay.Delay20,
            BitDepthEnum bitDepthEnum = BitDepthEnum.Float32)
        {
            this.frameDelay = (int) frameDelay;
            this.sampleRate = (int) sampleRate;
            this.bitRate = (int) bitRate;
            this.channels = (int) channels;
            this.bitDepth = (int) bitDepthEnum;

            this.Encoder = new(this.sampleRate, this.channels, this.bitRate, this.frameDelay);

            Encode = bitDepthEnum switch
            {
                BitDepthEnum.Float32 => Encoder.EncodeFloat,
                BitDepthEnum.UInt16 => Encoder.Encode,
                _ => throw new NotSupportedException(nameof(BitDepth))
            };

            _arrayPool = ArrayPool<byte>.Shared;

            // static RTP header fields
            _rtpHeader[0] = 0x80; // version + flags
            _rtpHeader[1] = 0x78; // payload type
        }

        public int SendPcmFrame(VoiceGateway gw, Span<byte> data, int offset, int count)
        {
            var secretKey = gw.SecretKey;
            if (secretKey.Length == 0)
                return (int) SendPcmError.SecretKeyUnavailable;

            var encodeOutput = _arrayPool.Rent(LibOpusEncoder.MaxData);
            try
            {
                var encodeOutputLength = Encode(data, encodeOutput);
                return SendOpusFrame(gw, encodeOutput, 0, encodeOutputLength);
            }
            finally
            {
                 _arrayPool.Return(encodeOutput);
            }
        }

        public int SendOpusFrame(VoiceGateway gw, byte[] data, int offset, int count)
        {
            var secretKey = gw.SecretKey;
            if (secretKey is null || secretKey.Length != Sodium.KEY_SIZE)
                return (int) SendPcmError.SecretKeyUnavailable;

            byte[] audioPayload;
            int audioPayloadLength;

            var daveManager = gw.DaveManager;
            if (daveManager != null && daveManager.HasKeyRatchet)
            {
                daveManager.AssignSsrcToCodec(gw.Ssrc);
                var maxEncSize = daveManager.GetMaxCiphertextSize(count);
                var encryptedFrame = _arrayPool.Rent(maxEncSize);
                try
                {
                    var encLen = daveManager.Encrypt(gw.Ssrc, data, count, encryptedFrame, maxEncSize);
                    if (encLen <= 0)
                    {
                        audioPayload = data;
                        audioPayloadLength = count;
                    }
                    else
                    {
                        audioPayload = new byte[encLen];
                        Buffer.BlockCopy(encryptedFrame, 0, audioPayload, 0, encLen);
                        audioPayloadLength = encLen;
                    }
                }
                finally
                {
                    _arrayPool.Return(encryptedFrame);
                }
            }
            else if (daveManager != null && gw.DaveProtocolVersion > 0)
            {
                return (int)SendPcmError.DaveKeyRatchetUnavailable;
            }
            else
            {
                audioPayload = data;
                audioPayloadLength = count;
            }

            // Write RTP header fields into pre-allocated buffer
            BinaryPrimitives.WriteUInt16BigEndian(_rtpHeader.AsSpan(2), gw.Sequence);
            BinaryPrimitives.WriteUInt32BigEndian(_rtpHeader.AsSpan(4), gw.Timestamp);
            BinaryPrimitives.WriteUInt32BigEndian(_rtpHeader.AsSpan(8), gw.Ssrc);

            gw.Timestamp += (uint) FrameSizePerChannel;
            gw.Sequence++;

            // Write 4-byte counter into pre-allocated nonce (remaining bytes are already zero)
            BinaryPrimitives.WriteUInt32BigEndian(_nonce.AsSpan(0), gw.NonceSequence);
            gw.NonceSequence++;

            // Encrypt: ciphertext = encrypted audio + 16-byte tag
            var encryptedLength = audioPayloadLength + Sodium.TAG_SIZE;
            var rtpDataLength = RtpHeaderLength + encryptedLength + NonceSuffixSize;
            var rtpData = _arrayPool.Rent(rtpDataLength);
            try
            {
                // Encrypt directly into the rtpData buffer after the header
                Sodium.Encrypt(
                    audioPayload, 0, audioPayloadLength,
                    rtpData, RtpHeaderLength,
                    _rtpHeader, RtpHeaderLength,
                    _nonce,
                    secretKey);

                // Copy RTP header
                Buffer.BlockCopy(_rtpHeader, 0, rtpData, 0, RtpHeaderLength);
                // Copy 4-byte nonce suffix (already big-endian in _nonce)
                Buffer.BlockCopy(_nonce, 0, rtpData, RtpHeaderLength + encryptedLength, NonceSuffixSize);

                gw.SendRtpData(rtpData, rtpDataLength);

                return rtpDataLength;
            }
            finally
            {
                _arrayPool.Return(rtpData);
            }
        }

        public void Dispose()
            => Encoder.Dispose();
    }

    public enum SendPcmError
    {
        SecretKeyUnavailable = -1,
        DaveKeyRatchetUnavailable = -2,
    }

    public enum FrameDelay
    {
        Delay5 = 5,
        Delay10 = 10,
        Delay20 = 20,
        Delay40 = 40,
        Delay60 = 60,
    }

    public enum BitDepthEnum
    {
        UInt16 = sizeof(UInt16),
        Float32 = sizeof(float),
    }

    public enum SampleRate
    {
        _48k = 48_000,
    }

    public enum Bitrate
    {
        _64k = 64 * 1024,
        _96k = 96 * 1024,
        _128k = 128 * 1024,
        _192k = 192 * 1024,
    }

    public enum Channels
    {
        One = 1,
        Two = 2,
    }
}
