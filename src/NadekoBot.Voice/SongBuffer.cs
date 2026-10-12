using Serilog;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NadekoBot.Voice
{
    public interface ISongBuffer : IDisposable
    {
        Span<byte> Read(int toRead, out int read);
        Task<bool> BufferAsync(ITrackDataSource source, CancellationToken cancellationToken);
        void Reset();
        void Stop();
    }

    public interface ITrackDataSource
    {
        public int Read(byte[] output);
    }

    public sealed class FfmpegTrackDataSource : ITrackDataSource, IDisposable
    {
        private readonly Process _p;

        // Some environments make ffmpeg skip its proxy for matching hosts.
        private static readonly string[] _proxyBypassEnvVars = ["no_proxy", "NO_PROXY"];

        private FfmpegTrackDataSource(int bitDepth, string streamUrl, bool isLocal, string? httpProxy)
        {
            var pcmType = bitDepth == 16 ? "s16le" : "f32le";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
            };

            string[] args = [
                ..(!isLocal ? [
                    "-reconnect", "1",
                    "-reconnect_streamed", "1",
                    "-reconnect_delay_max", "5",
                    "-probesize", "32768",
                    "-analyzeduration", "0"
                ] : (string[])[]),
                // A proxy which checks each address stops redirects and DNS rebinding to the local network.
                ..(!isLocal && httpProxy is not null ? ["-http_proxy", httpProxy] : (string[])[]),
                // Without a whitelist a stream or playlist can make ffmpeg read local files or other protocols.
                "-protocol_whitelist", isLocal
                    ? "file"
                    : httpProxy is null
                        ? "http,https,tcp,tls,crypto"
                        : "http,https,tcp,tls,crypto,httpproxy",
                "-err_detect", "ignore_err",
                "-i", streamUrl,
                "-f", pcmType,
                "-ar", "48000",
                "-vn", "-ac", "2",
                "pipe:1",
                "-loglevel", "error"
            ];

            foreach (var a in args)
                psi.ArgumentList.Add(a);

            if (httpProxy is not null)
            {
                foreach (var name in _proxyBypassEnvVars)
                    psi.Environment.Remove(name);
            }

            _p = Process.Start(psi)!;
        }

        public static FfmpegTrackDataSource? Create(int bitDepth, string streamUrl, bool isLocal, string? httpProxy)
        {
            try
            {
                return new FfmpegTrackDataSource(bitDepth, streamUrl, isLocal, httpProxy);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log.Error(@"You have not properly installed or configured FFMPEG. 
Please install and configure FFMPEG to play music. 
Check the guides for your platform on how to setup ffmpeg correctly:
    Windows Guide: https://goo.gl/OjKk8F
    Linux Guide:  https://goo.gl/ShjCUo");
                throw;
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                Log.Information(ex, "Error starting ffmpeg: {ErrorMessage}", ex.Message);
            }

            return null;
        }

        public int Read(byte[] output)
            => _p.StandardOutput.BaseStream.Read(output);

        public void Dispose()
        {
            try { _p?.Kill(); } catch { }
            try { _p?.Dispose(); } catch { }
        }
    }
}