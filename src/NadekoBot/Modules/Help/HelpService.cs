using NadekoBot.Common.Configs;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Help.Services;

public sealed class HelpService(
    BotConfigService bss,
    IReplacementService repSvc,
    IMessageSenderService sender,
    TimeProvider? timeProvider = null) : IExecNoCommand, IReadyExecutor, INService
{
    public const int DM_HELP_COOLDOWN_SECONDS = 10;
    private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(DM_HELP_COOLDOWN_SECONDS);
    private static readonly TimeSpan _pruneInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Discord sends every DM to shard 0, so one process sees all of them.
    private readonly ConcurrentDictionary<ulong, long> _lastReply = new();

    public Task OnReadyAsync()
    {
        _ = Task.Run(PruneLoopAsync);
        return Task.CompletedTask;
    }

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (guild is not null)
            return;

        var settings = bss.Data;
        if (!TryStartReplyInternal(settings, msg.Author.Id, msg.Content))
            return;

        var prefix = settings.Prefix;
        var repCtx = new ReplacementContext(guild: guild, channel: msg.Channel, user: msg.Author)
                     .WithOverride("%prefix%", () => prefix)
                     .WithOverride("%bot.prefix%", () => prefix);

        var text = SmartText.CreateFrom(settings.DmHelpText);
        text = await repSvc.ReplaceAsync(text, repCtx);

        try
        {
            await sender.Response(msg.Channel).Text(text).SendAsync();
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            Log.Warning("Discord refused the DM help text for user {UserId}", msg.Author.Id);
        }
    }

    // The cooldown stays after a failed reply, because a refused reply fails again and counts as an invalid request.
    public bool TryStartReply(ulong userId, string? content)
        => TryStartReplyInternal(bss.Data, userId, content);

    private bool TryStartReplyInternal(BotConfig settings, ulong userId, string? content)
    {
        if (string.IsNullOrWhiteSpace(settings.DmHelpText) || settings.DmHelpText == "-")
            return false;

        if (!MatchesKeyword(content, settings.DmHelpTextKeywords))
            return false;

        var now = _time.GetTimestamp();
        while (true)
        {
            if (!_lastReply.TryGetValue(userId, out var last))
            {
                if (_lastReply.TryAdd(userId, now))
                    return true;

                continue;
            }

            if (_time.GetElapsedTime(last, now) < _cooldown)
                return false;

            if (_lastReply.TryUpdate(userId, now, last))
                return true;
        }
    }

    private static bool MatchesKeyword(string? content, List<string>? keywords)
    {
        if (keywords is null || keywords.Count == 0)
            return true;

        if (string.IsNullOrEmpty(content))
            return false;

        foreach (var keyword in keywords)
        {
            if (keyword is not null && content.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task PruneLoopAsync()
    {
        using var timer = new PeriodicTimer(_pruneInterval, _time);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                PruneInternal();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error pruning the DM help cooldowns");
            }
        }
    }

    private void PruneInternal()
    {
        var now = _time.GetTimestamp();
        foreach (var (userId, last) in _lastReply)
        {
            if (_time.GetElapsedTime(last, now) >= _cooldown)
                _lastReply.TryRemove(new KeyValuePair<ulong, long>(userId, last));
        }
    }
}
