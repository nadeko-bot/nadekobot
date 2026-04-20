using System.Collections.Frozen;
using System.Threading.Channels;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.UserNotifications.Db;

namespace NadekoBot.Modules.Utility.UserNotifications;

public sealed class UserNotifyService(
    DbService db,
    DiscordSocketClient client,
    IMessageSenderService mss,
    IBotCache cache,
    IBotStrings bs,
    BotConfigService bcs,
    IEnumerable<IUserNotifyEventRegistrar> registrars
) : INService, IReadyExecutor
{
    private const int MAX_FAILURES = 3;

    private readonly Channel<NotifyMessage> _queue =
        Channel.CreateBounded<NotifyMessage>(new BoundedChannelOptions(300)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private FrozenDictionary<string, UserNotifyEventInfo>? _events;
    private UserNotifyEventInfo[]? _allEvents;

    private static TypedKey<int> FailKey(ulong userId)
        => new($"unotify:fail:{userId}");

    private static TypedKey<bool> CooldownKey(ulong userId)
        => new($"unotify:cd:{userId}");

    private void EnsureInitialized()
    {
        if (_events is not null)
            return;

        var all = new List<UserNotifyEventInfo>();
        foreach (var registrar in registrars)
        {
            foreach (var evt in registrar.GetEvents())
                all.Add(evt);
        }

        _allEvents = all.ToArray();
        _events = all.ToFrozenDictionary(
            static x => x.Key,
            static x => x,
            StringComparer.InvariantCultureIgnoreCase);
    }

    public Task OnReadyAsync()
    {
        _ = Task.Run(SendLoopAsync);
        return Task.CompletedTask;
    }

    private async Task SendLoopAsync()
    {
        await foreach (var msg in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await DeliverInternalAsync(msg);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in user notification send loop");
            }

            await Task.Delay(1000);
        }
    }

    private async Task DeliverInternalAsync(NotifyMessage msg)
    {
        var cdResult = await cache.GetAsync(CooldownKey(msg.UserId));
        if (cdResult.TryPickT0(out _, out _))
            return;

        try
        {
            var user = await client.Rest.GetUserAsync(msg.UserId);
            if (user is null)
                return;

            AttachToggleHintInternal(msg);

            await mss.Response(user).Embed(msg.Embed).SendAsync();

            await cache.RemoveAsync(FailKey(msg.UserId));
        }
        catch (Discord.Net.HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            await HandleDmFailureInternalAsync(msg.UserId);
        }
    }

    private void AttachToggleHintInternal(NotifyMessage msg)
    {
        EnsureInitialized();

        if (!_events!.TryGetValue(msg.Key, out var info))
            return;

        if (msg.Embed.Footer is not null)
            return;

        var nameLoc = info.Name;
        var name = bs.GetText(nameLoc.Key, (ulong?)null, nameLoc.Params);
        var prefix = bcs.Data.Prefix;
        var hintLoc = strs.user_notify_hint(prefix, name);
        var hint = bs.GetText(hintLoc.Key, (ulong?)null, hintLoc.Params);

        msg.Embed.WithFooter(hint);
    }

    private async Task HandleDmFailureInternalAsync(ulong userId)
    {
        var failResult = await cache.GetAsync(FailKey(userId));
        var fails = failResult.TryPickT0(out var f, out _) ? f + 1 : 1;

        if (fails >= MAX_FAILURES)
        {
            await DisableAllInternalAsync(userId);
            await cache.RemoveAsync(FailKey(userId));
            await cache.RemoveAsync(CooldownKey(userId));
            Log.Information("Disabled all notifications for {UserId} after {Failures} DM failures",
                userId, fails);
            return;
        }

        var cooldown = TimeSpan.FromHours(Math.Pow(4, fails - 1));
        await cache.AddAsync(FailKey(userId), fails, TimeSpan.FromDays(7));
        await cache.AddAsync(CooldownKey(userId), true, cooldown);
    }

    private async Task DisableAllInternalAsync(ulong userId)
    {
        EnsureInitialized();

        await using var ctx = db.GetDbContext();
        foreach (var evt in _allEvents!)
        {
            await ctx.GetTable<UserNotifyBlock>()
                .InsertOrUpdateAsync(
                    () => new() { UserId = userId, Type = evt.Key },
                    _ => new() { },
                    () => new() { UserId = userId, Type = evt.Key });
        }
    }

    public Task DisableAllAsync(ulong userId)
        => DisableAllInternalAsync(userId);

    public async Task EnableAllAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        await ctx.GetTable<UserNotifyBlock>()
            .Where(x => x.UserId == userId)
            .DeleteAsync();
    }

    public IReadOnlyList<UserNotifyEventInfo> GetAllEvents()
    {
        EnsureInitialized();
        return _allEvents!;
    }

    public bool IsValidKey(string key)
    {
        EnsureInitialized();
        return _events!.ContainsKey(key);
    }

    public async ValueTask NotifyAsync(ulong userId, string key, EmbedBuilder embed)
    {
        if (await IsBlockedAsync(userId, key))
            return;

        _queue.Writer.TryWrite(new(userId, key, embed));
    }

    public async Task<bool> ToggleAsync(ulong userId, string key)
    {
        await using var ctx = db.GetDbContext();

        var deleted = await ctx.GetTable<UserNotifyBlock>()
            .Where(x => x.UserId == userId && x.Type == key)
            .DeleteAsync();

        if (deleted > 0)
            return true;

        await ctx.GetTable<UserNotifyBlock>()
            .InsertAsync(() => new()
            {
                UserId = userId,
                Type = key
            });

        return false;
    }

    public async Task<HashSet<string>> GetBlockedAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var blocked = await ctx.GetTable<UserNotifyBlock>()
            .Where(x => x.UserId == userId)
            .Select(x => x.Type)
            .ToListAsyncLinqToDB();

        return blocked.ToHashSet(StringComparer.InvariantCultureIgnoreCase);
    }

    public async Task<bool> IsBlockedAsync(ulong userId, string key)
    {
        await using var ctx = db.GetDbContext();

        return await ctx.GetTable<UserNotifyBlock>()
            .AnyAsyncLinqToDB(x => x.UserId == userId && x.Type == key);
    }

    private readonly record struct NotifyMessage(
        ulong UserId,
        string Key,
        EmbedBuilder Embed);
}
