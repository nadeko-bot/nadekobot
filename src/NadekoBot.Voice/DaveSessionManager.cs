using System;
using System.Collections.Generic;
using Serilog;

namespace NadekoBot.Voice
{
    /// <summary>
    /// Manages the DAVE E2EE protocol state machine for a voice connection.
    /// Follows the reference implementation from libdave/samples/typescript/DaveSessionManager.ts.
    /// </summary>
    public sealed class DaveSessionManager : IDisposable
    {
        private const int INIT_TRANSITION_ID = 0;
        private const uint MLS_NEW_GROUP_EXPECTED_EPOCH = 1;

        private readonly ulong _guildId;
        private readonly string _selfUserId;
        private readonly DaveSession _session;
        private readonly HashSet<string> _recognizedUserIds = new();
        private readonly Dictionary<int, int> _protocolTransitions = new();
        private int _latestPreparedVersion;
        private bool _disposed;

        private static readonly LibDave.LogSinkCallback _nativeLogCallback = OnNativeLog;
        private static bool _logCallbackSet;

        public DaveSessionManager(ulong guildId, ulong userId)
        {
            _guildId = guildId;
            _selfUserId = userId.ToString();
            _session = new DaveSession();

            if (!_logCallbackSet)
            {
                LibDave.SetLogSinkCallback(_nativeLogCallback);
                _logCallbackSet = true;
            }
        }

        public bool HasKeyRatchet => _session.HasKeyRatchet();

        public void AssignSsrcToCodec(uint ssrc)
            => _session.AssignSsrcToCodec(ssrc);

        public int Encrypt(uint ssrc, byte[] frame, int frameLength, byte[] output, int outputCapacity)
            => _session.Encrypt(ssrc, frame, frameLength, output, outputCapacity);

        public int GetMaxCiphertextSize(int frameSize)
            => _session.GetMaxCiphertextSize(frameSize);

        /// <summary>
        /// Called when SessionDescription (op 4) is received with dave_protocol_version.
        /// Matches TS reference: onSelectProtocolAck
        /// </summary>
        public bool OnSessionDescription(int protocolVersion)
        {
            return HandleProtocolInit(protocolVersion);
        }

        public void OnPrepareTransition(int transitionId, int protocolVersion)
        {
            PrepareRatchets(transitionId, protocolVersion);
        }

        public void OnExecuteTransition(int transitionId)
        {
            HandleExecuteTransition(transitionId);
        }

        /// <summary>
        /// Called when PrepareEpoch (op 24) is received.
        /// Matches TS reference: onDaveProtocolPrepareEpoch
        /// </summary>
        public bool OnPrepareEpoch(uint epoch, int protocolVersion)
        {
            HandlePrepareEpoch(epoch, protocolVersion);
            return epoch == MLS_NEW_GROUP_EXPECTED_EPOCH;
        }

        public void OnExternalSender(byte[] externalSenderPackage)
        {
            _session.SetExternalSender(externalSenderPackage);
        }

        public byte[]? OnProposals(byte[] proposals)
        {
            return _session.ProcessProposals(proposals, GetRecognizedUserIds());
        }

        public CommitProcessResult OnCommitTransition(int transitionId, byte[] commit)
        {
            var result = _session.ProcessCommit(commit);
            if (result == CommitProcessResult.Success)
            {
                var version = _session.GetProtocolVersion();
                PrepareRatchets(transitionId, version);
            }

            return result;
        }

        /// <returns>True if welcome was processed successfully</returns>
        public bool OnWelcome(int transitionId, byte[] welcome)
        {
            var success = _session.ProcessWelcome(welcome, GetRecognizedUserIds());
            if (success)
            {
                var version = _session.GetProtocolVersion();
                PrepareRatchets(transitionId, version);
                return true;
            }

            return false;
        }

        public byte[]? GetKeyPackage()
            => _session.GetKeyPackage();

        public void AddUser(string userId)
        {
            _recognizedUserIds.Add(userId);
        }

        public void RemoveUser(string userId)
        {
            _recognizedUserIds.Remove(userId);
        }

        /// <summary>
        /// Matches TS reference: _handleDaveProtocolInit.
        /// Returns true if a key package should be sent.
        /// </summary>
        public bool HandleProtocolInit(int protocolVersion)
        {
            if (protocolVersion > 0)
            {
                HandlePrepareEpoch(MLS_NEW_GROUP_EXPECTED_EPOCH, protocolVersion);
                return true;
            }
            else
            {
                PrepareRatchets(INIT_TRANSITION_ID, protocolVersion);
                HandleExecuteTransition(INIT_TRANSITION_ID);
                return false;
            }
        }

        /// <summary>
        /// Matches TS reference: _handleDaveProtocolPrepareEpoch
        /// </summary>
        private void HandlePrepareEpoch(uint epoch, int protocolVersion)
        {
            if (epoch == MLS_NEW_GROUP_EXPECTED_EPOCH)
            {
                _session.Init((ushort)protocolVersion, _guildId, _selfUserId);
            }
        }

        private void HandleExecuteTransition(int transitionId)
        {
            if (!_protocolTransitions.TryGetValue(transitionId, out var protocolVersion))
            {
                Log.Warning("DAVE execute transition {TransitionId} not found in pending transitions", transitionId);
                return;
            }

            _protocolTransitions.Remove(transitionId);

            if (protocolVersion == 0)
            {
                _session.Reset();
            }

            _session.UpdateSelfKeyRatchet(_selfUserId);
        }

        private void PrepareRatchets(int transitionId, int protocolVersion)
        {
            if (transitionId == INIT_TRANSITION_ID)
            {
                _session.UpdateSelfKeyRatchet(_selfUserId);
            }
            else
            {
                _protocolTransitions[transitionId] = protocolVersion;
            }

            _latestPreparedVersion = protocolVersion;
        }

        private string[] GetRecognizedUserIds()
        {
            var list = new List<string>(_recognizedUserIds);
            if (!list.Contains(_selfUserId))
                list.Add(_selfUserId);
            return list.ToArray();
        }

        private static void OnNativeLog(int severity, string file, int line, string message)
        {
            if (severity >= 2)
                Log.Warning("[libdave] {Message}", message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
