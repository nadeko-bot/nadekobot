using System;
using System.Collections.Generic;
using Serilog;

namespace NadekoBot.Voice
{
    /// <summary>
    /// Manages the DAVE E2EE protocol state machine for a voice connection.
    /// </summary>
    public sealed class DaveSessionManager : IDisposable
    {
        private const int INIT_TRANSITION_ID = 0;
        private const uint MLS_NEW_GROUP_EXPECTED_EPOCH = 1;

        private readonly ulong _channelId;
        private readonly string _selfUserId;
        private readonly DaveSession _session;
        private readonly HashSet<string> _recognizedUserIds = new();
        private readonly Dictionary<int, int> _protocolTransitions = new();
        private int _latestPreparedVersion;
        private int? _lastTransitionId;
        private bool _reinitializing;
        private bool _disposed;

        private static readonly LibDave.LogSinkCallback _nativeLogCallback = OnNativeLog;
        private static bool _logCallbackSet;

        public DaveSessionManager(ulong channelId, ulong userId)
        {
            _channelId = channelId;
            _selfUserId = userId.ToString();
            _session = new DaveSession();

            if (!_logCallbackSet)
            {
                LibDave.SetLogSinkCallback(_nativeLogCallback);
                _logCallbackSet = true;
            }
        }

        public bool HasKeyRatchet => _session.HasKeyRatchet();

        public bool IsReinitializing => _reinitializing;

        public int? LastTransitionId => _lastTransitionId;

        public void AssignSsrcToCodec(uint ssrc)
            => _session.AssignSsrcToCodec(ssrc);

        public int Encrypt(uint ssrc, byte[] frame, int frameLength, byte[] output, int outputCapacity)
            => _session.Encrypt(ssrc, frame, frameLength, output, outputCapacity);

        public int GetMaxCiphertextSize(int frameSize)
            => _session.GetMaxCiphertextSize(frameSize);

        /// <summary>
        /// Called when SessionDescription (op 4) is received with dave_protocol_version.
        /// </summary>
        public bool OnSessionDescription(int protocolVersion)
        {
            Log.Information("DAVE Manager: OnSessionDescription protocolVersion={ProtocolVersion}", protocolVersion);
            return HandleProtocolInit(protocolVersion);
        }

        /// <summary>
        /// Called when DAVE_PREPARE_TRANSITION (op 21) is received.
        /// Returns true if executed immediately (transitionId==0), meaning caller should NOT send TRANSITION_READY.
        /// </summary>
        public bool OnPrepareTransition(int transitionId, int protocolVersion)
        {
            _protocolTransitions[transitionId] = protocolVersion;

            if (transitionId == INIT_TRANSITION_ID)
            {
                Log.Information("DAVE Manager: PrepareTransition init (transitionId=0), executing immediately");
                HandleExecuteTransition(transitionId);
                return true;
            }

            Log.Information("DAVE Manager: PrepareTransition transitionId={TransitionId}, protocolVersion={ProtocolVersion}",
                transitionId, protocolVersion);
            return false;
        }

        /// <summary>
        /// Called when DAVE_EXECUTE_TRANSITION (op 22) is received.
        /// Returns the new protocol version, or -1 if the transition was not found.
        /// </summary>
        public int OnExecuteTransition(int transitionId)
        {
            Log.Information("DAVE Manager: OnExecuteTransition transitionId={TransitionId}", transitionId);
            return HandleExecuteTransition(transitionId);
        }

        /// <summary>
        /// Called when PrepareEpoch (op 24) is received.
        /// </summary>
        public bool OnPrepareEpoch(uint epoch, int protocolVersion)
        {
            Log.Information("DAVE Manager: OnPrepareEpoch epoch={Epoch}, protocolVersion={ProtocolVersion}", epoch, protocolVersion);
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
            Log.Information("DAVE Manager: OnCommitTransition transitionId={TransitionId}, result={Result}", transitionId, result);
            if (result == CommitProcessResult.Success)
            {
                if (transitionId != INIT_TRANSITION_ID)
                {
                    var version = _session.GetProtocolVersion();
                    PrepareRatchets(transitionId, version);
                }
                else
                {
                    _session.UpdateSelfKeyRatchet(_selfUserId);
                    _reinitializing = false;
                }

                _lastTransitionId = transitionId;
            }

            return result;
        }

        /// <returns>True if welcome was processed successfully</returns>
        public bool OnWelcome(int transitionId, byte[] welcome)
        {
            var success = _session.ProcessWelcome(welcome, GetRecognizedUserIds());
            Log.Information("DAVE Manager: OnWelcome transitionId={TransitionId}, success={Success}", transitionId, success);
            if (success)
            {
                if (transitionId != INIT_TRANSITION_ID)
                {
                    var version = _session.GetProtocolVersion();
                    PrepareRatchets(transitionId, version);
                }
                else
                {
                    _session.UpdateSelfKeyRatchet(_selfUserId);
                    _reinitializing = false;
                }

                _lastTransitionId = transitionId;
                return true;
            }

            return false;
        }

        public byte[]? GetKeyPackage()
        {
            return _session.GetKeyPackage();
        }

        public void AddUser(string userId)
        {
            _recognizedUserIds.Add(userId);
        }

        public void RemoveUser(string userId)
        {
            _recognizedUserIds.Remove(userId);
        }

        /// <summary>
        /// Returns true if a key package should be sent.
        /// </summary>
        public bool HandleProtocolInit(int protocolVersion, bool isRecovery = false)
        {
            Log.Information("DAVE Manager: HandleProtocolInit protocolVersion={ProtocolVersion}, isRecovery={IsRecovery}",
                protocolVersion, isRecovery);

            if (isRecovery)
                _reinitializing = true;

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

        private void HandlePrepareEpoch(uint epoch, int protocolVersion)
        {
            if (epoch == MLS_NEW_GROUP_EXPECTED_EPOCH)
            {
                _session.Init((ushort)protocolVersion, _channelId, _selfUserId);
            }
        }

        private int HandleExecuteTransition(int transitionId)
        {
            if (!_protocolTransitions.TryGetValue(transitionId, out var protocolVersion))
            {
                Log.Warning("DAVE Manager: ExecuteTransition {TransitionId} not found in pending transitions", transitionId);
                return -1;
            }

            _protocolTransitions.Remove(transitionId);

            if (protocolVersion == 0)
            {
                Log.Information("DAVE Manager: ExecuteTransition resetting session (protocolVersion=0)");
                _session.Reset();
            }

            _session.UpdateSelfKeyRatchet(_selfUserId);
            Log.Information("DAVE Manager: ExecuteTransition transitionId={TransitionId} complete, hasKeyRatchet={HasRatchet}",
                transitionId, _session.HasKeyRatchet());
            _reinitializing = false;
            _lastTransitionId = transitionId;
            return protocolVersion;
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
            var hasSelf = _recognizedUserIds.Contains(_selfUserId);
            var count = _recognizedUserIds.Count + (hasSelf ? 0 : 1);
            var result = new string[count];
            _recognizedUserIds.CopyTo(result);
            if (!hasSelf)
                result[count - 1] = _selfUserId;
            return result;
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
