#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using System.Diagnostics;

namespace NadekoBot.Services;

public sealed class StatsService : IStatsService, IReadyExecutor, INService
{
    public static string BotVersion
        => typeof(Bot).Assembly.GetName().Version?.ToString(3) ?? "Custom";

    public string Author
        => "Kwoth#2452";

    public double MessagesPerSecond
        => MessageCounter / GetUptime().TotalSeconds;

    public long TextChannels
        => Interlocked.Read(ref textChannels);

    public long VoiceChannels
        => Interlocked.Read(ref voiceChannels);

    public long MessageCounter
        => Interlocked.Read(ref messageCounter);

    public long CommandsRan
        => Interlocked.Read(ref commandsRan);

    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;
    private readonly DateTime _started;

    private long textChannels;
    private long voiceChannels;
    private long messageCounter;
    private long commandsRan;

    private readonly IHttpClientFactory _httpFactory;

    public StatsService(
        DiscordSocketClient client,
        CommandHandler cmdHandler,
        IBotCreds creds,
        IHttpClientFactory factory)
    {
        _client = client;
        _creds = creds;
        _httpFactory = factory;

        _started = DateTime.UtcNow;
        _client.MessageReceived += _ => Task.FromResult(Interlocked.Increment(ref messageCounter));
        cmdHandler.CommandExecuted += (_, _) => Task.FromResult(Interlocked.Increment(ref commandsRan));

        _client.ChannelCreated += c =>
        {
            if (c is IVoiceChannel)
                Interlocked.Increment(ref voiceChannels);
            else if (c is ITextChannel)
                Interlocked.Increment(ref textChannels);

            return Task.CompletedTask;
        };

        _client.ChannelDestroyed += c =>
        {
            if (c is IVoiceChannel)
                Interlocked.Decrement(ref voiceChannels);
            else if (c is ITextChannel)
                Interlocked.Decrement(ref textChannels);

            return Task.CompletedTask;
        };

        _client.GuildAvailable += g =>
        {
            var tc = g.Channels.Count(cx => cx is ITextChannel and not IVoiceChannel);
            var vc = g.Channels.Count(cx => cx is IVoiceChannel);
            Interlocked.Add(ref textChannels, tc);
            Interlocked.Add(ref voiceChannels, vc);

            return Task.CompletedTask;
        };

        _client.JoinedGuild += g =>
        {
            var tc = g.Channels.Count(cx => cx is ITextChannel and not IVoiceChannel);
            var vc = g.Channels.Count(cx => cx is IVoiceChannel);
            Interlocked.Add(ref textChannels, tc);
            Interlocked.Add(ref voiceChannels, vc);

            return Task.CompletedTask;
        };

        _client.GuildUnavailable += g =>
        {
            var tc = g.Channels.Count(cx => cx is ITextChannel and not IVoiceChannel);
            var vc = g.Channels.Count(cx => cx is IVoiceChannel);
            Interlocked.Add(ref textChannels, -tc);
            Interlocked.Add(ref voiceChannels, -vc);

            return Task.CompletedTask;
        };

        _client.LeftGuild += g =>
        {
            var tc = g.Channels.Count(cx => cx is ITextChannel and not IVoiceChannel);
            var vc = g.Channels.Count(cx => cx is IVoiceChannel);
            Interlocked.Add(ref textChannels, -tc);
            Interlocked.Add(ref voiceChannels, -vc);

            return Task.CompletedTask;
        };
    }

    private void InitializeChannelCount()
    {
        var guilds = _client.Guilds;
        textChannels = guilds.Sum(static g => g.Channels.Count(static cx => cx is ITextChannel and not IVoiceChannel));
        voiceChannels = guilds.Sum(static g => g.Channels.Count(static cx => cx is IVoiceChannel));
    }

    public async Task OnReadyAsync()
    {
        InitializeChannelCount();

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            if (string.IsNullOrWhiteSpace(_creds.BotListToken))
                continue;

            try
            {
                using var http = _httpFactory.CreateClient();
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "shard_count", _creds.TotalShards.ToString() },
                    { "shard_id", _client.ShardId.ToString() },
                    { "server_count", _client.Guilds.Count().ToString() }
                });
                content.Headers.Clear();
                content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                http.DefaultRequestHeaders.Add("Authorization", _creds.BotListToken);

                using var res = await http.PostAsync(
                    new Uri($"https://discordbots.org/api/bots/{_client.CurrentUser.Id}/stats"),
                    content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in botlist post");
            }
        } while (await timer.WaitForNextTickAsync());
    }

    public TimeSpan GetUptime()
        => DateTime.UtcNow - _started;

    public string GetUptimeString(string separator = ", ")
    {
        var time = GetUptime();

        if (time.Days > 0)
            return $"{time.Days}d {time.Hours}h {time.Minutes}m";

        if (time.Hours > 0)
            return $"{time.Hours}h {time.Minutes}m";

        if (time.Minutes > 0)
            return $"{time.Minutes}m {time.Seconds}s";

        return $"{time.Seconds}s";
    }

    public double GetPrivateMemoryMegabytes()
    {
        _currentProcess.Refresh();
        return _currentProcess.PrivateMemorySize64 / 1.Megabytes();
    }

    public async Task<GuildInfo> GetGuildInfoAsync(ulong id)
    {
        var g = _client.GetGuild(id);
        var ig = (IGuild)g;

        return new GuildInfo()
        {
            Id = g.Id,
            IconUrl = g.IconUrl,
            Name = g.Name,
            Owner = (await ig.GetUserAsync(g.OwnerId))?.Username ?? "??Unknown",
            OwnerId = g.OwnerId,
            CreatedAt = g.CreatedAt.UtcDateTime,
            VoiceChannels = g.VoiceChannels.Count,
            TextChannels = g.TextChannels.Count,
            Features = g.Features.Value.ToString().Split(","),
            Emojis = g.Emotes.ToArray(),
            Roles = g.Roles.OrderByDescending(x => x.Position).ToArray(),
            MemberCount = g.MemberCount,
        };
    }
}