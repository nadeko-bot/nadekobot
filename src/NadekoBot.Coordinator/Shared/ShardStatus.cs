using System;
using System.Diagnostics;

namespace NadekoBot.Coordinator
{
    public enum ConnState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
    }

    public sealed record ShardStatus(
        int ShardId,
        DateTime LastUpdate,
        int GuildCount = 0,
        ConnState State = ConnState.Disconnected,
        bool ShouldRestart = false,
        Process Process = null,
        int StateCounter = 0,
        double UptimePercent = 0,
        string Token = null
    )
    {
        public DateTime? StartedAtUtc()
        {
            try
            {
                return Process is null or { HasExited: true }
                    ? null
                    : Process.StartTime.ToUniversalTime();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }
}
