using NadekoBot.Voice.Models;
using Discord.Models.Gateway;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ayu.Discord.Gateway;
using Newtonsoft.Json;

namespace NadekoBot.Voice
{
    public class VoiceGateway
    {
        private class QueueItem
        {
            public VoicePayload Payload { get; }
            public TaskCompletionSource<bool> Result { get; }

            public QueueItem(VoicePayload payload, TaskCompletionSource<bool> result)
            {
                Payload = payload;
                Result = result;
            }
        }

        private readonly ulong _guildId;
        private readonly ulong _userId;
        private readonly string _sessionId;
        private readonly string _token;
        private readonly string _endpoint;
        private readonly Uri _websocketUrl;
        private readonly Channel<QueueItem> _channel;

        public TaskCompletionSource<bool> ConnectingFinished { get; }

        private readonly Random _rng;
        private readonly SocketClient _ws;
        private readonly UdpClient _udpClient;
        private Timer? _heartbeatTimer;
        private bool _receivedAck;
        private IPEndPoint? _udpEp;

        public uint Ssrc { get; private set; }
        public string Ip { get; private set; } = string.Empty;
        public int Port { get; private set; } = 0;
        public byte[] SecretKey { get; private set; } = Array.Empty<byte>();
        public string Mode { get; private set; } = string.Empty;
        public ushort Sequence { get; set; }
        public uint NonceSequence { get; set; }
        public uint Timestamp { get; set; }
        public string MyIp { get; private set; } = string.Empty;
        public ushort MyPort { get; private set; }
        private bool _shouldResume;
        private int _lastSeqAck = -1;

        public int DaveProtocolVersion { get; private set; }
        public DaveSessionManager? DaveManager { get; private set; }
        
        private readonly CancellationTokenSource _stopCancellationSource;
        private readonly CancellationToken _stopCancellationToken;
        public bool Stopped => _stopCancellationToken.IsCancellationRequested;

        public event Func<VoiceGateway, Task> OnClosed = delegate { return Task.CompletedTask; };

        public VoiceGateway(ulong guildId, ulong userId, string session, string token, string endpoint)
        {
            this._guildId = guildId;
            this._userId = userId;
            this._sessionId = session;
            this._token = token;
            this._endpoint = endpoint;

            this._websocketUrl = new($"wss://{_endpoint.Replace(":80", "")}?v=8");
            this._channel = Channel.CreateUnbounded<QueueItem>(new()
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

            ConnectingFinished = new();

            _rng = new();

            _ws = new();
            _udpClient = new();
            _stopCancellationSource = new();
            _stopCancellationToken = _stopCancellationSource.Token;

            _ws.PayloadReceived += _ws_PayloadReceived;
            _ws.BinaryPayloadReceived += _ws_BinaryPayloadReceived;
            _ws.WebsocketClosed += _ws_WebsocketClosed;
        }

        public Task WaitForReadyAsync()
            => ConnectingFinished.Task;

        private async Task SendLoop()
        {
            while (!_stopCancellationToken.IsCancellationRequested)
            {
                try
                {
                    var qi = await _channel.Reader.ReadAsync(_stopCancellationToken);

                    var json = JsonConvert.SerializeObject(qi.Payload);

                    if (!_stopCancellationToken.IsCancellationRequested)
                        await _ws.SendAsync(Encoding.UTF8.GetBytes(json));
                    _ = Task.Run(() => qi.Result.TrySetResult(true));
                }
                catch (ChannelClosedException)
                {
                    Log.Warning("Voice gateway send channel is closed");
                }
            }
        }

        private void TrackSeqFromJson(JObject? root)
        {
            if (root != null && root.TryGetValue("seq", out var seqToken))
            {
                var seq = seqToken.Value<int>();
                if (seq > _lastSeqAck)
                    _lastSeqAck = seq;
            }
        }

        private async Task _ws_PayloadReceived(byte[] arg)
        {
            var jsonStr = Encoding.UTF8.GetString(arg);
            var root = JsonConvert.DeserializeObject<JObject>(jsonStr);
            if (root is null)
                return;

            TrackSeqFromJson(root);

            var payload = root.ToObject<VoicePayload>();
            if (payload is null)
                return;
            try
            {
                switch (payload.OpCode)
                {
                    case VoiceOpCode.Identify:
                    case VoiceOpCode.SelectProtocol:
                    case VoiceOpCode.Heartbeat:
                    case VoiceOpCode.Resume:
                        break;
                    case VoiceOpCode.Ready:
                        var ready = payload.Data.ToObject<VoiceReady>();
                        await HandleReadyAsync(ready!);
                        _shouldResume = true;
                        break;
                    case VoiceOpCode.SessionDescription:
                        var sd = payload.Data.ToObject<VoiceSessionDescription>();
                        await HandleSessionDescription(sd!);
                        break;
                    case VoiceOpCode.Speaking:
                        break;
                    case VoiceOpCode.HeartbeatAck:
                        _receivedAck = true;
                        break;
                    case VoiceOpCode.Hello:
                        var hello = payload.Data.ToObject<VoiceHello>();
                        await HandleHelloAsync(hello!);
                        break;
                    case VoiceOpCode.Resumed:
                        _shouldResume = true;
                        break;
                    case VoiceOpCode.ClientsConnect:
                        HandleClientsConnect(payload.Data);
                        break;
                    case VoiceOpCode.ClientDisconnect:
                        HandleClientDisconnect(payload.Data);
                        break;
                    case VoiceOpCode.DavePrepareTransition:
                        HandleDavePrepareTransitionJson(payload.Data);
                        break;
                    case VoiceOpCode.DaveExecuteTransition:
                        HandleDaveExecuteTransitionJson(payload.Data);
                        break;
                    case VoiceOpCode.DavePrepareEpoch:
                        HandleDavePrepareEpochJson(payload.Data);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling payload with opcode {OpCode}: {Message}", payload.OpCode, ex.Message);
            }
        }

        private Task _ws_BinaryPayloadReceived(byte[] data)
        {
            if (data.Length < 3)
                return Task.CompletedTask;

            try
            {
                var seqNum = (ushort)((data[0] << 8) | data[1]);
                if (seqNum > _lastSeqAck || (_lastSeqAck > 60000 && seqNum < 5000))
                    _lastSeqAck = seqNum;

                var opcode = (VoiceOpCode)data[2];
                var payload = new byte[data.Length - 3];
                if (payload.Length > 0)
                    Buffer.BlockCopy(data, 3, payload, 0, payload.Length);

                if (DaveManager is null)
                    return Task.CompletedTask;

                switch (opcode)
                {
                    case VoiceOpCode.DaveMlsExternalSender:
                        DaveManager.OnExternalSender(payload);
                        break;
                    case VoiceOpCode.DaveMlsProposals:
                        HandleDaveMlsProposals(payload);
                        break;
                    case VoiceOpCode.DaveMlsAnnounceCommitTransition:
                        HandleDaveMlsAnnounceCommitTransition(payload);
                        break;
                    case VoiceOpCode.DaveMlsWelcome:
                        HandleDaveMlsWelcome(payload);
                        break;
                    default:
                        Log.Debug("Unhandled binary voice opcode: {Opcode}", opcode);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling binary voice payload: {Message}", ex.Message);
            }

            return Task.CompletedTask;
        }

        private void HandleClientsConnect(JToken data)
        {
            if (DaveManager is null) return;
            var userIds = data?["user_ids"];
            if (userIds is null) return;
            foreach (var uid in userIds)
            {
                var id = uid.Value<string>();
                if (id != null)
                    DaveManager.AddUser(id);
            }
        }

        private void HandleClientDisconnect(JToken data)
        {
            if (DaveManager is null) return;
            var userId = data?["user_id"]?.Value<string>();
            if (userId != null)
                DaveManager.RemoveUser(userId);
        }

        private void HandleDavePrepareTransitionJson(JToken data)
        {
            if (DaveManager is null) return;
            var transitionId = data?["transition_id"]?.Value<int>() ?? 0;
            var protocolVersion = data?["protocol_version"]?.Value<int>() ?? 0;
            DaveManager.OnPrepareTransition(transitionId, protocolVersion);
            SendDaveTransitionReady(transitionId);
        }

        private void HandleDaveExecuteTransitionJson(JToken data)
        {
            if (DaveManager is null) return;
            var transitionId = data?["transition_id"]?.Value<int>() ?? 0;
            DaveManager.OnExecuteTransition(transitionId);
        }

        private void HandleDavePrepareEpochJson(JToken data)
        {
            if (DaveManager is null) return;
            var epoch = data?["epoch"]?.Value<uint>() ?? 0;
            var protocolVersion = data?["protocol_version"]?.Value<int>() ?? 0;
            var needsKeyPackage = DaveManager.OnPrepareEpoch(epoch, protocolVersion);

            if (needsKeyPackage)
            {
                SendDaveMlsKeyPackage();
            }
        }

        private void HandleDaveMlsProposals(byte[] payload)
        {
            var commitWelcome = DaveManager!.OnProposals(payload);
            if (commitWelcome != null && commitWelcome.Length > 0)
            {
                SendDaveBinaryOpcode(VoiceOpCode.DaveMlsCommitWelcome, commitWelcome);
            }
        }

        private void HandleDaveMlsAnnounceCommitTransition(byte[] payload)
        {
            if (payload.Length < 2)
                return;

            var transitionId = (ushort)((payload[0] << 8) | payload[1]);
            var commit = new byte[payload.Length - 2];
            if (commit.Length > 0)
                Buffer.BlockCopy(payload, 2, commit, 0, commit.Length);

            var result = DaveManager!.OnCommitTransition(transitionId, commit);
            if (result == CommitProcessResult.Success)
            {
                SendDaveTransitionReady(transitionId);
            }
            else if (result == CommitProcessResult.Failed)
            {
                Log.Warning("DAVE commit failed for transition {TransitionId}, recovering", transitionId);
                SendDaveInvalidCommitWelcome(transitionId);
                var needsKeyPackage = DaveManager.HandleProtocolInit(DaveProtocolVersion);
                if (needsKeyPackage)
                    SendDaveMlsKeyPackage();
            }
            else if (result == CommitProcessResult.Ignored)
            {
                Log.Warning("DAVE commit ignored for transition {TransitionId}, recovering", transitionId);
                SendDaveInvalidCommitWelcome(transitionId);
                var needsKeyPackage = DaveManager.HandleProtocolInit(DaveProtocolVersion);
                if (needsKeyPackage)
                    SendDaveMlsKeyPackage();
            }
        }

        private void HandleDaveMlsWelcome(byte[] payload)
        {
            if (payload.Length < 2)
                return;

            var transitionId = (ushort)((payload[0] << 8) | payload[1]);
            var welcome = new byte[payload.Length - 2];
            if (welcome.Length > 0)
                Buffer.BlockCopy(payload, 2, welcome, 0, welcome.Length);

            var success = DaveManager!.OnWelcome(transitionId, welcome);
            if (success)
            {
                SendDaveTransitionReady(transitionId);
            }
            else
            {
                Log.Warning("DAVE welcome failed for transition {TransitionId}, recovering", transitionId);
                SendDaveInvalidCommitWelcome(transitionId);
                SendDaveMlsKeyPackage();
            }
        }

        private void SendDaveTransitionReady(int transitionId)
        {
            if (transitionId != 0)
            {
                _ = SendCommandPayloadAsync(new()
                {
                    OpCode = VoiceOpCode.DaveTransitionReady,
                    Data = JToken.FromObject(new { transition_id = transitionId })
                });
            }
        }

        private void SendDaveInvalidCommitWelcome(int transitionId)
        {
            _ = SendCommandPayloadAsync(new()
            {
                OpCode = VoiceOpCode.DaveMlsInvalidCommitWelcome,
                Data = JToken.FromObject(new { transition_id = transitionId })
            });
        }

        private void SendDaveMlsKeyPackage()
        {
            var keyPackage = DaveManager?.GetKeyPackage();
            if (keyPackage != null && keyPackage.Length > 0)
            {
                SendDaveBinaryOpcode(VoiceOpCode.DaveMlsKeyPackage, keyPackage);
            }
        }

        private void SendDaveBinaryOpcode(VoiceOpCode opcode, byte[] payload)
        {
            var message = new byte[1 + payload.Length];
            message[0] = (byte)opcode;
            if (payload.Length > 0)
                Buffer.BlockCopy(payload, 0, message, 1, payload.Length);

            _ = _ws.SendBulkAsync(message);
        }

        private Task _ws_WebsocketClosed(string arg)
        {
            if (!string.IsNullOrWhiteSpace(arg))
            {
                Log.Warning("Voice Websocket closed: {Arg}", arg);
            }

            var hbt = _heartbeatTimer;
            hbt?.Change(Timeout.Infinite, Timeout.Infinite);
            _heartbeatTimer = null;

            if (!_stopCancellationToken.IsCancellationRequested && _shouldResume)
            {
                _ = _ws.RunAndBlockAsync(_websocketUrl, _stopCancellationToken);
                return Task.CompletedTask;
            }
            
            _ws.WebsocketClosed -= _ws_WebsocketClosed;
            _ws.PayloadReceived -= _ws_PayloadReceived;
            _ws.BinaryPayloadReceived -= _ws_BinaryPayloadReceived;
            
            if(!_stopCancellationToken.IsCancellationRequested)
                _stopCancellationSource.Cancel();

            DaveManager?.Dispose();
            DaveManager = null;

            return this.OnClosed(this);
        }

        public void SendRtpData(byte[] rtpData, int length)
            => _udpClient.Send(rtpData, length, _udpEp);

        private Task HandleSessionDescription(VoiceSessionDescription sd)
        {
            SecretKey = sd.SecretKey;
            Mode = sd.Mode;
            DaveProtocolVersion = sd.DaveProtocolVersion;

            if (DaveProtocolVersion > 0 && DaveManager != null)
            {
                var needsKeyPackage = DaveManager.OnSessionDescription(DaveProtocolVersion);
                if (needsKeyPackage)
                    SendDaveMlsKeyPackage();
            }

            _ = Task.Run(() => ConnectingFinished.TrySetResult(true));

            return Task.CompletedTask;
        }

        private Task ResumeAsync()
        {
            _shouldResume = false;
            return SendCommandPayloadAsync(new()
            {
                OpCode = VoiceOpCode.Resume,
                Data = JToken.FromObject(new VoiceResume
                {
                    ServerId = this._guildId.ToString(),
                    SessionId = this._sessionId,
                    Token = this._token,
                    SeqAck = this._lastSeqAck,
                })
            });
        }

        private async Task HandleReadyAsync(VoiceReady ready)
        {
            Ssrc = ready.Ssrc;

            _udpEp = new(IPAddress.Parse(ready.Ip), ready.Port);

            var ssrcBytes = BitConverter.GetBytes(Ssrc);
            Array.Reverse(ssrcBytes);
            var ipDiscoveryData = new byte[74];
            Buffer.BlockCopy(ssrcBytes, 0, ipDiscoveryData, 4, ssrcBytes.Length);
            ipDiscoveryData[0] = 0x00;
            ipDiscoveryData[1] = 0x01;
            ipDiscoveryData[2] = 0x00;
            ipDiscoveryData[3] = 0x46;
            await _udpClient.SendAsync(ipDiscoveryData, ipDiscoveryData.Length, _udpEp);
            while (true)
            {
                var buffer = _udpClient.Receive(ref _udpEp);

                if (buffer.Length == 74)
                {
                    var myIp = Encoding.UTF8.GetString(buffer, 8, buffer.Length - 10);
                    MyIp = myIp.TrimEnd('\0');
                    MyPort = (ushort)((buffer[^2] << 8) | buffer[^1]);

                    await SelectProtocol();
                    return;
                }
            }
        }

        private Task HandleHelloAsync(VoiceHello data)
        {
            _receivedAck = true;
            _heartbeatTimer = new(async _ =>
            {
                await SendHeartbeatAsync();
            }, default, data.HeartbeatInterval, data.HeartbeatInterval);

            if (_shouldResume)
            {
                return ResumeAsync();
            }

            DaveManager?.Dispose();
            DaveManager = new DaveSessionManager(_guildId, _userId);

            return IdentifyAsync();
        }

        private Task IdentifyAsync()
            => SendCommandPayloadAsync(new()
            {
                OpCode = VoiceOpCode.Identify,
                Data = JToken.FromObject(new VoiceIdentify
                {
                    ServerId = _guildId.ToString(),
                    SessionId = _sessionId,
                    Token = _token,
                    UserId = _userId.ToString(),
                    MaxDaveProtocolVersion = 1,
                })
            });

        private Task SelectProtocol()
            => SendCommandPayloadAsync(new()
            {
                OpCode = VoiceOpCode.SelectProtocol,
                Data = JToken.FromObject(new SelectProtocol
                {
                    Protocol = "udp",
                    Data = new()
                    {
                        Address = MyIp,
                        Port = MyPort,
                        Mode = "aead_xchacha20_poly1305_rtpsize",
                    }
                })
            });

        private async Task SendHeartbeatAsync()
        {
            if (!_receivedAck)
            {
                Log.Warning("Voice gateway didn't receive HearbeatAck - closing");
                var success = await _ws.CloseAsync();
                if (!success)
                    await _ws_WebsocketClosed(null);
                return;
            }
            
            _receivedAck = false;
            await SendCommandPayloadAsync(new()
            {
                OpCode = VoiceOpCode.Heartbeat,
                Data = JToken.FromObject(new
                {
                    t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    seq_ack = _lastSeqAck,
                })
            });
        }

        public Task SendSpeakingAsync(VoiceSpeaking.State speaking)
            => SendCommandPayloadAsync(new()
            {
                OpCode = VoiceOpCode.Speaking,
                Data = JToken.FromObject(new VoiceSpeaking
                {
                    Delay = 0,
                    Ssrc = Ssrc,
                    Speaking = (int)speaking
                })
            });

        public Task StopAsync()
        {
            Started = false;
            _shouldResume = false;
            if(!_stopCancellationSource.IsCancellationRequested)
                try { _stopCancellationSource.Cancel(); } catch { }
            return _ws.CloseAsync("Stopped by the user.");
        }

        public Task Start()
        {
            Started = true;
            _ = SendLoop();
            return _ws.RunAndBlockAsync(_websocketUrl, _stopCancellationToken);
        }

        public bool Started { get; set; }

        public async Task SendCommandPayloadAsync(VoicePayload payload)
        {
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queueItem = new QueueItem(payload, complete);

            if (!_channel.Writer.TryWrite(queueItem))
                await _channel.Writer.WriteAsync(queueItem);

            await complete.Task;
        }
    }
}