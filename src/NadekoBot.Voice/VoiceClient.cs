using System;
using System.Buffers;

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

        public int BitDepth => bitDepth * 8;
        public int Delay => frameDelay;

        private int FrameSizePerChannel => Encoder.FrameSizePerChannel;
        public int InputLength => FrameSizePerChannel * channels * bitDepth;

        EncodeDelegate Encode;

        // https://github.com/xiph/opus/issues/42 w
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

            if (bitDepthEnum == BitDepthEnum.Float32)
            {
                Encode = Encoder.EncodeFloat;
            }
            else
            {
                Encode = Encoder.Encode;
            }

            _arrayPool = ArrayPool<byte>.Shared;
        }

        public int SendPcmFrame(VoiceGateway gw, Span<byte> data, int offset, int count)
        {
            var secretKey = gw.SecretKey;
            if (secretKey.Length == 0)
            {
                return (int) SendPcmError.SecretKeyUnavailable;
            }

            // encode using opus
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
            if (secretKey is null)
            {
                return (int) SendPcmError.SecretKeyUnavailable;
            }

            // form RTP header
            var headerLength = 1 // version + flags
                               + 1 // payload type
                               + 2 // sequence
                               + 4 // timestamp
                               + 4; // ssrc

            var header = new byte[headerLength];

            header[0] = 0x80; // version + flags
            header[1] = 0x78; // payload type

            // get byte values for header data
            var seqBytes = BitConverter.GetBytes(gw.Sequence); // 2
            var nonceBytes = BitConverter.GetBytes(gw.NonceSequence); // 2
            var timestampBytes = BitConverter.GetBytes(gw.Timestamp); // 4
            var ssrcBytes = BitConverter.GetBytes(gw.Ssrc); // 4

            gw.Timestamp += (uint) FrameSizePerChannel;
            gw.Sequence++;
            gw.NonceSequence++;

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(seqBytes);
                Array.Reverse(nonceBytes);
                Array.Reverse(timestampBytes);
                Array.Reverse(ssrcBytes);
            }

            // copy headers
            Buffer.BlockCopy(seqBytes, 0, header, 2, 2);
            Buffer.BlockCopy(timestampBytes, 0, header, 4, 4);
            Buffer.BlockCopy(ssrcBytes, 0, header, 8, 4);

            //// encryption part
            //// create a byte array where to store the encrypted data
            //// it has to be inputLength + crypto_secretbox_MACBYTES (constant with value 16)
            var encryptedBytes = new byte[count + 16];

            //// form nonce with header + 12 empty bytes
            //var nonce = new byte[24];
            //Buffer.BlockCopy(rtpHeader, 0, nonce, 0, rtpHeader.Length);

            var nonce = new byte[4];
            Buffer.BlockCopy(seqBytes, 0, nonce, 2, 2);

            Sodium.Encrypt(data, 0, count, encryptedBytes, 0, nonce, secretKey);

            var rtpDataLength = headerLength + encryptedBytes.Length + nonce.Length;
            var rtpData = _arrayPool.Rent(rtpDataLength);
            try
            {
                //copy headers
                Buffer.BlockCopy(header, 0, rtpData, 0, header.Length);
                //copy audio data 
                Buffer.BlockCopy(encryptedBytes, 0, rtpData, header.Length, encryptedBytes.Length);
                Buffer.BlockCopy(nonce, 0, rtpData, rtpDataLength - 4, 4);

                gw.SendRtpData(rtpData, rtpDataLength);
                // FUTURE When there's a break in the sent data,
                // the packet transmission shouldn't simply stop.
                // Instead, send five frames of silence (0xF8, 0xFF, 0xFE)
                // before stopping to avoid unintended Opus interpolation
                // with subsequent transmissions.

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