using System;
using System.Runtime.InteropServices;

namespace NadekoBot.Voice
{
    internal static unsafe class LibOpus
    {
        public const string OPUS = "data/lib/opus";

        [DllImport(OPUS, EntryPoint = "opus_encoder_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CreateEncoder(int Fs, int channels, int application, out OpusError error);

        [DllImport(OPUS, EntryPoint = "opus_encoder_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DestroyEncoder(IntPtr encoder);

        [DllImport(OPUS, EntryPoint = "opus_encode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Encode(IntPtr st, byte* pcm, int frame_size, byte* data, int max_data_bytes);

        [DllImport(OPUS, EntryPoint = "opus_encode_float", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int EncodeFloat(IntPtr st, byte* pcm, int frame_size, byte* data, int max_data_bytes);

        [DllImport(OPUS, EntryPoint = "opus_encoder_ctl", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int EncoderCtl(IntPtr st, OpusCtl request, int value);
    }

    public enum OpusApplication
    {
        VOIP = 2048,
        Audio = 2049,
        RestrictedLowdelay = 2051
    }

    public unsafe class LibOpusEncoder : IDisposable
    {
        private readonly IntPtr _encoderPtr;

        private readonly int _sampleRate;

        // private readonly int _channels;
        // private readonly int _bitRate;
        private readonly int _frameDelay;

        private readonly int _frameSizePerChannel;
        public int FrameSizePerChannel => _frameSizePerChannel;

        public const int MaxData = 1276;

        public LibOpusEncoder(int sampleRate, int channels, int bitRate, int frameDelay)
        {
            _sampleRate = sampleRate;
            // _channels = channels;
            // _bitRate = bitRate;
            _frameDelay = frameDelay;
            _frameSizePerChannel = _sampleRate * _frameDelay / 1000;

            _encoderPtr = LibOpus.CreateEncoder(sampleRate, channels, (int) OpusApplication.Audio, out var error);
            if (error != OpusError.OK)
                throw new ExternalException(error.ToString());

            LibOpus.EncoderCtl(_encoderPtr, OpusCtl.SetSignal, (int) OpusSignal.Music);
            LibOpus.EncoderCtl(_encoderPtr, OpusCtl.SetInbandFEC, 1);
            LibOpus.EncoderCtl(_encoderPtr, OpusCtl.SetBitrate, bitRate);
            LibOpus.EncoderCtl(_encoderPtr, OpusCtl.SetPacketLossPerc, 2);
        }

        public int SetControl(OpusCtl ctl, int value)
            => LibOpus.EncoderCtl(_encoderPtr, ctl, value);

        public int Encode(Span<byte> input, byte[] output)
        {
            fixed (byte* inPtr = input)
            fixed (byte* outPtr = output)
                return LibOpus.Encode(_encoderPtr, inPtr, FrameSizePerChannel, outPtr, output.Length);
        }

        public int EncodeFloat(Span<byte> input, byte[] output)
        {
            fixed (byte* inPtr = input)
            fixed (byte* outPtr = output)
                return LibOpus.EncodeFloat(_encoderPtr, inPtr, FrameSizePerChannel, outPtr, output.Length);
        }


        public void Dispose()
            => LibOpus.DestroyEncoder(_encoderPtr);
    }

    public enum OpusCtl
    {
        SetBitrate = 4002,
        GetBitrate = 4003,
        SetBandwidth = 4008,
        GetBandwidth = 4009,
        SetComplexity = 4010,
        GetComplexity = 4011,
        SetInbandFEC = 4012,
        GetInbandFEC = 4013,
        SetPacketLossPerc = 4014,
        GetPacketLossPerc = 4015,
        SetLsbDepth = 4036,
        GetLsbDepth = 4037,
        SetDtx = 4016,
        GetDtx = 4017,
        SetSignal = 4024
    }

    public enum OpusError
    {
        OK = 0,
        BadArg = -1,
        BufferToSmall = -2,
        InternalError = -3,
        InvalidPacket = -4,
        Unimplemented = -5,
        InvalidState = -6,
        AllocFail = -7
    }

    public enum OpusSignal
    {
        Auto = -1000,
        Voice = 3001,
        Music = 3002,
    }
}