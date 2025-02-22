using System;
using System.Runtime.InteropServices;

namespace NadekoBot.Voice
{
    internal static unsafe class Sodium
    {
        
        private const string SODIUM = "data/lib/libsodium";

        [DllImport(SODIUM, EntryPoint = "crypto_secretbox_easy", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SecretBoxEasy(byte* output, byte* input, long inputLength, byte* nonce, byte* secret);
        [DllImport(SODIUM, EntryPoint = "crypto_secretbox_open_easy", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SecretBoxOpenEasy(byte* output, byte* input, ulong inputLength, byte* nonce, byte* secret);

        public static int Encrypt(byte[] input, int inputOffset, long inputLength, byte[] output, int outputOffset, in ReadOnlySpan<byte> nonce, byte[] secret)
        {
            fixed (byte* inPtr = input)
            fixed (byte* outPtr = output)
            fixed (byte* noncePtr = nonce)
            fixed (byte* secretPtr = secret)
                return SecretBoxEasy(outPtr + outputOffset, inPtr + inputOffset, inputLength - inputOffset, noncePtr, secretPtr);
        }
        public static int Decrypt(byte[] input, ulong inputLength, byte[] output, in ReadOnlySpan<byte> nonce, byte[] secret)
        {
            fixed (byte* outPtr = output)
            fixed (byte* inPtr = input)
            fixed (byte* noncePtr = nonce)
            fixed (byte* secretPtr = secret)
                return SecretBoxOpenEasy(outPtr, inPtr, inputLength, noncePtr, secretPtr);
        }
    }
}
