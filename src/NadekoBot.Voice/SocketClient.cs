using Serilog;
using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ayu.Discord.Gateway
{
    public class SocketClient : IDisposable
    {
        private ClientWebSocket? _ws;

        public event Func<byte[], Task>? PayloadReceived = delegate { return Task.CompletedTask; };
        public event Func<string, Task>? WebsocketClosed = delegate { return Task.CompletedTask; };

        const int CHUNK_SIZE = 1024 * 16;

        public async Task RunAndBlockAsync(Uri url, CancellationToken cancel)
        {
            var error = "Error.";
            var bufferWriter = new ArrayBufferWriter<byte>(CHUNK_SIZE);
            try
            {
                using (_ws = new())
                {
                    await _ws.ConnectAsync(url, cancel).ConfigureAwait(false);
                    // WebsocketConnected!.Invoke(this);

                    while (true)
                    {
                        var result = await _ws.ReceiveAsync(bufferWriter.GetMemory(CHUNK_SIZE), cancel);
                        bufferWriter.Advance(result.Count);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            var closeMessage = CloseCodes.GetErrorCodeMessage((int?) _ws.CloseStatus ?? 0).Message;
                            error = $"Websocket closed ({_ws.CloseStatus}): {_ws.CloseStatusDescription} {closeMessage}";
                            break;
                        }

                        if (result.EndOfMessage)
                        {
                            var pr = PayloadReceived;
                            var data = bufferWriter.WrittenMemory.ToArray();
                            bufferWriter.Clear();

                            if (pr is not null)
                            {
                                await pr.Invoke(data);
                            }
                        }
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Log.Warning("Disconnected, check your internet connection...");
                Log.Debug(ex, "Websocket Exception in websocket client");
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in websocket client. {Message}", ex.Message);
            }
            finally
            {
                bufferWriter.Clear();
                _ws = null;
                await ClosedAsync(error).ConfigureAwait(false);
            }
        }

        private async Task ClosedAsync(string msg = "Error")
        {
            try
            {
                await WebsocketClosed!.Invoke(msg).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public async Task SendAsync(byte[] data)
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var ws = _ws;
                if (ws is null)
                    throw new WebSocketException("Websocket is disconnected.");
                for (var i = 0; i < data.Length; i += 4096)
                {
                    var count = i + 4096 > data.Length ? data.Length - i : 4096;
                    await ws.SendAsync(new(data, i, count),
                        WebSocketMessageType.Text,
                        i + count >= data.Length,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task SendBulkAsync(byte[] data)
        {
            var ws = _ws;
            if (ws is null)
                throw new WebSocketException("Websocket is disconnected.");

            await ws.SendAsync(new(data, 0, data.Length),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<bool> CloseAsync(string msg = "Stop")
        {
            if (_ws is not null && _ws.State != WebSocketState.Closed)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.InternalServerError, msg, CancellationToken.None)
                        .ConfigureAwait(false);
                    
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        public void Dispose()
        {
            PayloadReceived = null;
            WebsocketClosed = null;
            var ws = _ws;
            if (ws is null)
                return;

            ws.Dispose();
        }
    }
}