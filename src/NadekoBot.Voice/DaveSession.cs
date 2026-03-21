using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Serilog;

namespace NadekoBot.Voice
{
    public enum CommitProcessResult
    {
        Success,
        Ignored,
        Failed
    }

    internal sealed class DaveSession : IDisposable
    {
        private IntPtr _session;
        private IntPtr _encryptor;
        private IntPtr _selfKeyRatchet;
        private readonly LibDave.MlsFailureCallback _failureCallback;
        private bool _disposed;

        public DaveSession()
        {
            _failureCallback = OnMlsFailure;
            _session = LibDave.SessionCreate(IntPtr.Zero, null, _failureCallback, IntPtr.Zero);
            _encryptor = LibDave.EncryptorCreate();
        }

        public bool IsValid => _session != IntPtr.Zero;

        public void Init(ushort protocolVersion, ulong groupId, string selfUserId)
        {
            if (_session == IntPtr.Zero) return;
            LibDave.SessionInit(_session, protocolVersion, groupId, selfUserId);
        }

        public void Reset()
        {
            if (_session == IntPtr.Zero) return;
            LibDave.SessionReset(_session);
        }

        public ushort GetProtocolVersion()
        {
            if (_session == IntPtr.Zero) return 0;
            return LibDave.SessionGetProtocolVersion(_session);
        }

        public void SetExternalSender(byte[] externalSender)
        {
            if (_session == IntPtr.Zero) return;
            LibDave.SessionSetExternalSender(_session, externalSender, (UIntPtr)externalSender.Length);
        }

        public byte[]? GetKeyPackage()
        {
            if (_session == IntPtr.Zero) return null;

            LibDave.SessionGetMarshalledKeyPackage(_session, out var ptr, out var length);
            if (ptr == IntPtr.Zero || (int)length == 0)
                return null;

            var result = new byte[(int)length];
            Marshal.Copy(ptr, result, 0, result.Length);
            LibDave.Free(ptr);
            return result;
        }

        public byte[]? ProcessProposals(byte[] proposals, string[] recognizedUserIds)
        {
            if (_session == IntPtr.Zero) return null;

            var userIdPtrs = MarshalStringArray(recognizedUserIds);
            try
            {
                LibDave.SessionProcessProposals(
                    _session,
                    proposals,
                    (UIntPtr)proposals.Length,
                    userIdPtrs,
                    (UIntPtr)userIdPtrs.Length,
                    out var commitWelcomePtr,
                    out var commitWelcomeLen);

                if (commitWelcomePtr == IntPtr.Zero || (int)commitWelcomeLen == 0)
                    return null;

                var result = new byte[(int)commitWelcomeLen];
                Marshal.Copy(commitWelcomePtr, result, 0, result.Length);
                LibDave.Free(commitWelcomePtr);
                return result;
            }
            finally
            {
                FreeStringArray(userIdPtrs);
            }
        }

        public CommitProcessResult ProcessCommit(byte[] commit)
        {
            if (_session == IntPtr.Zero) return CommitProcessResult.Failed;

            var result = LibDave.SessionProcessCommit(_session, commit, (UIntPtr)commit.Length);
            if (result == IntPtr.Zero) return CommitProcessResult.Failed;

            try
            {
                if (LibDave.CommitResultIsIgnored(result))
                    return CommitProcessResult.Ignored;
                if (LibDave.CommitResultIsFailed(result))
                    return CommitProcessResult.Failed;

                return CommitProcessResult.Success;
            }
            finally
            {
                LibDave.CommitResultDestroy(result);
            }
        }

        /// <returns>True if welcome was processed successfully</returns>
        public bool ProcessWelcome(byte[] welcome, string[] recognizedUserIds)
        {
            if (_session == IntPtr.Zero) return false;

            var userIdPtrs = MarshalStringArray(recognizedUserIds);
            try
            {
                var result = LibDave.SessionProcessWelcome(
                    _session,
                    welcome,
                    (UIntPtr)welcome.Length,
                    userIdPtrs,
                    (UIntPtr)userIdPtrs.Length);

                if (result == IntPtr.Zero) return false;

                LibDave.WelcomeResultDestroy(result);
                return true;
            }
            finally
            {
                FreeStringArray(userIdPtrs);
            }
        }

        public void UpdateSelfKeyRatchet(string selfUserId)
        {
            if (_session == IntPtr.Zero || _encryptor == IntPtr.Zero) return;

            if (_selfKeyRatchet != IntPtr.Zero)
            {
                LibDave.KeyRatchetDestroy(_selfKeyRatchet);
                _selfKeyRatchet = IntPtr.Zero;
            }

            _selfKeyRatchet = LibDave.SessionGetKeyRatchet(_session, selfUserId);
            if (_selfKeyRatchet != IntPtr.Zero)
            {
                LibDave.EncryptorSetKeyRatchet(_encryptor, _selfKeyRatchet);
                LibDave.EncryptorSetPassthroughMode(_encryptor, false);
            }
        }

        public void SetPassthroughMode(bool passthrough)
        {
            if (_encryptor == IntPtr.Zero) return;
            LibDave.EncryptorSetPassthroughMode(_encryptor, passthrough);
        }

        public void AssignSsrcToCodec(uint ssrc)
        {
            if (_encryptor == IntPtr.Zero) return;
            LibDave.EncryptorAssignSsrcToCodec(_encryptor, ssrc, LibDave.DAVE_CODEC_OPUS);
        }

        public bool HasKeyRatchet()
        {
            if (_encryptor == IntPtr.Zero) return false;
            return LibDave.EncryptorHasKeyRatchet(_encryptor);
        }

        public int Encrypt(uint ssrc, byte[] frame, int frameLength, byte[] output, int outputCapacity)
        {
            if (_encryptor == IntPtr.Zero) return -1;

            var result = LibDave.EncryptorEncrypt(
                _encryptor,
                LibDave.DAVE_MEDIA_TYPE_AUDIO,
                ssrc,
                frame,
                (UIntPtr)frameLength,
                output,
                (UIntPtr)outputCapacity,
                out var bytesWritten);

            if (result != LibDave.DAVE_ENCRYPTOR_RESULT_CODE_SUCCESS)
                return -1;

            return (int)bytesWritten;
        }

        public int GetMaxCiphertextSize(int frameSize)
        {
            if (_encryptor == IntPtr.Zero) return frameSize + 128;
            return (int)LibDave.EncryptorGetMaxCiphertextByteSize(
                _encryptor, LibDave.DAVE_MEDIA_TYPE_AUDIO, (UIntPtr)frameSize);
        }

        private static void OnMlsFailure(string source, string reason, IntPtr userData)
        {
            Log.Debug("DAVE MLS failure: {Source} - {Reason}", source, reason);
        }

        private static IntPtr[] MarshalStringArray(string[] strings)
        {
            var ptrs = new IntPtr[strings.Length];
            for (int i = 0; i < strings.Length; i++)
                ptrs[i] = Marshal.StringToHGlobalAnsi(strings[i]);
            return ptrs;
        }

        private static void FreeStringArray(IntPtr[] ptrs)
        {
            for (int i = 0; i < ptrs.Length; i++)
            {
                if (ptrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptrs[i]);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_selfKeyRatchet != IntPtr.Zero)
            {
                LibDave.KeyRatchetDestroy(_selfKeyRatchet);
                _selfKeyRatchet = IntPtr.Zero;
            }

            if (_encryptor != IntPtr.Zero)
            {
                LibDave.EncryptorDestroy(_encryptor);
                _encryptor = IntPtr.Zero;
            }

            if (_session != IntPtr.Zero)
            {
                LibDave.SessionDestroy(_session);
                _session = IntPtr.Zero;
            }
        }
    }
}
