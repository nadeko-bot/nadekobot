using System.Collections.Frozen;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public sealed class LogCommandService : ILogCommandService, IReadyExecutor
#if !GLOBAL_NADEKO
        , INService
#endif
{
    private ConcurrentDictionary<ulong, FrozenDictionary<LogType, ulong>> _logChannels = new();
    private ConcurrentDictionary<ulong, IReadOnlyList<LogIgnore>> _logIgnores = new();

    private ConcurrentDictionary<ITextChannel, System.Collections.Concurrent.ConcurrentQueue<string>> PresenceUpdates { get; } = new();
    private readonly DiscordSocketClient _client;

    private readonly IBotStrings _strings;
    private readonly DbService _db;
    private readonly MuteService _mute;
    private readonly ProtectionService _prot;
    private readonly GuildTimezoneService _tz;
    private readonly IMemoryCache _memoryCache;

    private readonly ConcurrentHashSet<ulong> _ignoreMessageIds = [];
    private readonly ConcurrentHashSet<(ulong GuildId, ulong UserId)> _ignoreBanIds = [];
    private readonly ConcurrentHashSet<(ulong GuildId, ulong UserId)> _ignoreUnbanIds = [];
    private readonly UserPunishService _punishService;
    private readonly IMessageSenderService _sender;

    public LogCommandService(
        DiscordSocketClient client,
        IBotStrings strings,
        DbService db,
        MuteService mute,
        ProtectionService prot,
        GuildTimezoneService tz,
        IMemoryCache memoryCache,
        UserPunishService punishService,
        IMessageSenderService sender)
    {
        _client = client;
        _memoryCache = memoryCache;
        _sender = sender;
        _strings = strings;
        _db = db;
        _mute = mute;
        _prot = prot;
        _tz = tz;
        _punishService = punishService;

        using (var uow = db.GetDbContext())
        {
            var guildIds = client.Guilds.Select(x => x.Id).ToList();

            var channels = uow.GetTable<LogChannel>()
                .Where(x => guildIds.Contains(x.GuildId))
                .ToList();

            _logChannels = channels
                .GroupBy(x => x.GuildId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToFrozenDictionary(x => x.LogType, x => x.ChannelId))
                .ToConcurrent();

            var ignores = uow.GetTable<LogIgnore>()
                .Where(x => guildIds.Contains(x.GuildId))
                .ToList();

            _logIgnores = ignores
                .GroupBy(x => x.GuildId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<LogIgnore>)g.ToList())
                .ToConcurrent();
        }

        _client.MessageUpdated += _client_MessageUpdated;
        _client.MessageDeleted += _client_MessageDeleted;
        _client.UserBanned += _client_UserBanned;
        _client.UserUnbanned += _client_UserUnbanned;
        _client.UserJoined += _client_UserJoined;
        _client.UserLeft += _client_UserLeft;
        _client.UserVoiceStateUpdated += _client_UserVoiceStateUpdated;
        _client.GuildMemberUpdated += _client_GuildUserUpdated;
        _client.PresenceUpdated += _client_PresenceUpdated;
        _client.UserUpdated += _client_UserUpdated;
        _client.ChannelCreated += _client_ChannelCreated;
        _client.ChannelDestroyed += _client_ChannelDestroyed;
        _client.ChannelUpdated += _client_ChannelUpdated;
        _client.RoleDeleted += _client_RoleDeleted;

        _client.ThreadCreated += _client_ThreadCreated;
        _client.ThreadDeleted += _client_ThreadDeleted;

        _mute.UserMuted += MuteCommands_UserMuted;
        _mute.UserUnmuted += MuteCommands_UserUnmuted;

        _prot.OnAntiProtectionTriggered += TriggeredAntiProtection;

        _punishService.OnUserWarned += PunishServiceOnOnUserWarned;
    }

    private bool TryGetLogChannelId(ulong guildId, LogType logType, out ulong channelId)
    {
        channelId = 0;
        return _logChannels.TryGetValue(guildId, out var dict) && dict.TryGetValue(logType, out channelId);
    }

    private bool IsUserIgnored(ulong guildId, ulong userId)
    {
        if (!_logIgnores.TryGetValue(guildId, out var ignores))
            return false;

        for (var i = 0; i < ignores.Count; i++)
        {
            if (ignores[i].LogItemId == userId && ignores[i].ItemType == IgnoredItemType.User)
                return true;
        }

        return false;
    }

    private bool IsChannelIgnored(ulong guildId, ulong channelId, ulong? categoryId)
    {
        if (!_logIgnores.TryGetValue(guildId, out var ignores))
            return false;

        for (var i = 0; i < ignores.Count; i++)
        {
            var ilc = ignores[i];
            if (ilc.LogItemId == channelId && ilc.ItemType == IgnoredItemType.Channel)
                return true;
            if (categoryId is not null && ilc.LogItemId == categoryId.Value && ilc.ItemType == IgnoredItemType.Category)
                return true;
        }

        return false;
    }

    private async Task<ITextChannel?> TryGetLogChannel(IGuild guild, LogType logType)
    {
        if (!TryGetLogChannelId(guild.Id, logType, out var channelId))
            return null;

        var channel = await guild.GetTextChannelAsync(channelId);

        if (channel is null)
        {
            await UnsetLogChannelInternalAsync(guild.Id, logType);
            return null;
        }

        return channel;
    }

    private async Task UnsetLogChannelInternalAsync(ulong guildId, LogType logType)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<LogChannel>()
            .Where(x => x.GuildId == guildId && x.LogType == logType)
            .DeleteAsync();

        RefreshLogChannelCacheInternal(guildId, ctx);
    }

    private void RefreshLogChannelCacheInternal(ulong guildId, NadekoContext ctx)
    {
        var channels = ctx.GetTable<LogChannel>()
            .Where(x => x.GuildId == guildId)
            .ToList();

        if (channels.Count == 0)
            _logChannels.TryRemove(guildId, out _);
        else
            _logChannels[guildId] = channels.ToFrozenDictionary(x => x.LogType, x => x.ChannelId);
    }

    private void RefreshIgnoreCacheInternal(ulong guildId, NadekoContext ctx)
    {
        var ignores = ctx.GetTable<LogIgnore>()
            .Where(x => x.GuildId == guildId)
            .ToList();

        if (ignores.Count == 0)
            _logIgnores.TryRemove(guildId, out _);
        else
            _logIgnores[guildId] = ignores;
    }

    private async Task _client_PresenceUpdated(SocketUser user, SocketPresence? before, SocketPresence? after)
    {
        if (user is not SocketGuildUser gu)
            return;

        if (!TryGetLogChannelId(gu.Guild.Id, LogType.UserPresence, out _)
            || before is null
            || after is null
            || IsUserIgnored(gu.Guild.Id, gu.Id))
            return;

        ITextChannel? logChannel;

        if (!user.IsBot
            && (logChannel = await TryGetLogChannel(gu.Guild, LogType.UserPresence)) is not null)
        {
            if (before.Status != after.Status)
            {
                var str = "🎭"
                          + Format.Code(PrettyCurrentTime(gu.Guild))
                          + GetText(logChannel.Guild,
                              strs.user_status_change("👤" + Format.Bold(gu.Username),
                                  Format.Bold(after.Status.ToString())));
                PresenceUpdates.GetOrAdd(logChannel, _ => new System.Collections.Concurrent.ConcurrentQueue<string>()).Enqueue(str);
            }
            else if (before.Activities.FirstOrDefault()?.Name != after.Activities.FirstOrDefault()?.Name)
            {
                var str =
                    $"👾`{PrettyCurrentTime(gu.Guild)}`👤__**{gu.Username}**__ is now playing **{after.Activities.FirstOrDefault()?.Name ?? "-"}**.";
                PresenceUpdates.GetOrAdd(logChannel, _ => new System.Collections.Concurrent.ConcurrentQueue<string>()).Enqueue(str);
            }
        }
    }

    private Task _client_ThreadDeleted(Cacheable<SocketThreadChannel, ulong> sch)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!sch.HasValue)
                    return;

                var ch = sch.Value;

                if (!TryGetLogChannelId(ch.Guild.Id, LogType.ThreadDeleted, out _)
                    || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch.ParentChannel as INestedChannel)?.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, LogType.ThreadDeleted)) is null)
                    return;

                var title = GetText(logChannel.Guild, strs.thread_deleted);

                await _sender.Response(logChannel).Embed(_sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("🗑 " + title)
                    .WithDescription($"{ch.Name} | {ch.Id}")
                    .WithFooter(CurrentTime(ch.Guild))).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_ThreadCreated(SocketThreadChannel ch)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!TryGetLogChannelId(ch.Guild.Id, LogType.ThreadCreated, out _)
                    || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch.ParentChannel as INestedChannel)?.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, LogType.ThreadCreated)) is null)
                    return;

                var title = GetText(logChannel.Guild, strs.thread_created);

                await _sender.Response(logChannel).Embed(_sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("🆕 " + title)
                    .WithDescription($"{ch.Name} | {ch.Id}")
                    .WithFooter(CurrentTime(ch.Guild))).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });

        return Task.CompletedTask;
    }

    public async Task OnReadyAsync()
        => await Task.WhenAll(PresenceUpdateTask(), IgnoreMessageIdsClearTask());

    private async Task IgnoreMessageIdsClearTask()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync())
            _ignoreMessageIds.Clear();
    }

    private async Task PresenceUpdateTask()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var keys = PresenceUpdates.Keys.ToList();

                await keys.Select(channel =>
                    {
                        if (!((SocketGuild)channel.Guild).CurrentUser.GetPermissions(channel).SendMessages)
                            return Task.CompletedTask;

                        if (PresenceUpdates.TryRemove(channel, out var msgs))
                        {
                            var title = GetText(channel.Guild, strs.presence_updates);
                            var desc = string.Join(Environment.NewLine, msgs);
                            return _sender.Response(channel).Confirm(title, desc.TrimTo(2048)!).SendAsync();
                        }

                        return Task.CompletedTask;
                    })
                    .WhenAll();
            }
            catch
            {
            }
        }
    }

    public IReadOnlyList<LogIgnore> GetLogIgnores(ulong guildId)
    {
        _logIgnores.TryGetValue(guildId, out var ignores);
        return ignores ?? [];
    }

    public ulong? GetLogChannelId(ulong guildId, LogType logType)
        => TryGetLogChannelId(guildId, logType, out var id) ? id : null;

    public void AddDeleteIgnore(ulong messageId)
        => _ignoreMessageIds.Add(messageId);

    public void AddBanIgnore(ulong guildId, ulong userId)
    {
        _ignoreBanIds.Add((guildId, userId));
        _ignoreUnbanIds.Add((guildId, userId));
    }

    public async Task LogHoneypot(IGuild guild, IUser user)
    {
        if (!TryGetLogChannelId(guild.Id, LogType.Honeypot, out _))
            return;

        ITextChannel? logChannel;
        if ((logChannel = await TryGetLogChannel(guild, LogType.Honeypot)) is null)
            return;

        var embed = _sender.CreateEmbed()
            .WithOkColor()
            .WithTitle("🍯 " + GetText(logChannel.Guild, strs.user_honeypot))
            .WithDescription(user.ToString()!)
            .AddField("Id", user.Id.ToString())
            .WithFooter(CurrentTime(guild));

        var avatarUrl = user.GetAvatarUrl();
        if (Uri.IsWellFormedUriString(avatarUrl, UriKind.Absolute))
            embed.WithThumbnailUrl(avatarUrl);

        await _sender.Response(logChannel).Embed(embed).SendAsync();
    }

    public bool LogIgnore(ulong guildId, ulong itemId, IgnoredItemType itemType)
    {
        using var uow = _db.GetDbContext();

        var deleted = uow.GetTable<LogIgnore>()
            .Where(x => x.GuildId == guildId && x.LogItemId == itemId && x.ItemType == itemType)
            .DeleteAsync()
            .GetAwaiter()
            .GetResult();

        if (deleted == 0)
        {
            uow.GetTable<LogIgnore>()
                .InsertAsync(() => new LogIgnore
                {
                    GuildId = guildId,
                    LogItemId = itemId,
                    ItemType = itemType
                })
                .GetAwaiter()
                .GetResult();
        }

        RefreshIgnoreCacheInternal(guildId, uow);
        return deleted > 0;
    }

    private string GetText(IGuild guild, LocStr str)
        => _strings.GetText(str, guild.Id);

    private string PrettyCurrentTime(IGuild? g)
    {
        var time = DateTime.UtcNow;
        if (g is not null)
            time = TimeZoneInfo.ConvertTime(time, _tz.GetTimeZoneOrUtc(g.Id));
        return $"【{time:HH:mm:ss}】";
    }

    private string CurrentTime(IGuild? g)
    {
        var time = DateTime.UtcNow;
        if (g is not null)
            time = TimeZoneInfo.ConvertTime(time, _tz.GetTimeZoneOrUtc(g.Id));

        return $"{time:HH:mm:ss}";
    }

    public async Task LogServer(ulong guildId, ulong channelId, bool value)
    {
        await using var uow = _db.GetDbContext();

        await uow.GetTable<LogChannel>()
            .Where(x => x.GuildId == guildId)
            .DeleteAsync();

        if (value)
        {
            var logTypes = Enum.GetValues<LogType>();
            var rows = logTypes.Select(t => new LogChannel
            {
                GuildId = guildId,
                LogType = t,
                ChannelId = channelId
            }).ToArray();

            await uow.BulkCopyAsync(rows);
        }

        RefreshLogChannelCacheInternal(guildId, uow);
    }

    private async Task PunishServiceOnOnUserWarned(Warning arg)
    {
        if (!TryGetLogChannelId(arg.GuildId, LogType.UserWarned, out _))
            return;

        var g = _client.GetGuild(arg.GuildId);

        ITextChannel? logChannel;
        if ((logChannel = await TryGetLogChannel(g, LogType.UserWarned)) is null)
            return;

        var embed = _sender.CreateEmbed()
            .WithOkColor()
            .WithTitle($"⚠️ User Warned")
            .WithDescription($"<@{arg.UserId}> | {arg.UserId}")
            .AddField("Mod", arg.Moderator)
            .AddField("Reason", string.IsNullOrWhiteSpace(arg.Reason) ? "-" : arg.Reason, true)
            .WithFooter(CurrentTime(g));

        await _sender.Response(logChannel).Embed(embed).SendAsync();
    }

    private Task _client_UserUpdated(SocketUser before, SocketUser uAfter)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (uAfter is not SocketGuildUser after)
                    return;

                var g = after.Guild;

                if (!TryGetLogChannelId(g.Id, LogType.UserUpdated, out _)
                    || IsUserIgnored(g.Id, after.Id))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(g, LogType.UserUpdated)) is null)
                    return;

                var embed = _sender.CreateEmbed();

                if (before.Username != after.Username)
                {
                    embed.WithTitle("👥 " + GetText(g, strs.username_changed))
                        .WithDescription($"{before.Username} | {before.Id}")
                        .AddField("Old Name", $"{before.Username}", true)
                        .AddField("New Name", $"{after.Username}", true)
                        .WithFooter(CurrentTime(g))
                        .WithOkColor();
                }
                else if (before.AvatarId != after.AvatarId)
                {
                    embed.WithTitle("👥" + GetText(g, strs.avatar_changed))
                        .WithDescription($"{before.Username}#{before.Discriminator} | {before.Id}")
                        .WithFooter(CurrentTime(g))
                        .WithOkColor();

                    var bav = before.RealAvatarUrl();
                    if (bav.IsAbsoluteUri)
                        embed.WithThumbnailUrl(bav.ToString());

                    var aav = after.RealAvatarUrl();
                    if (aav.IsAbsoluteUri)
                        embed.WithImageUrl(aav.ToString());
                }
                else
                    return;

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    public bool Log(ulong gid, ulong? cid, LogType type)
    {
        using var uow = _db.GetDbContext();

        var deleted = uow.GetTable<LogChannel>()
            .Where(x => x.GuildId == gid && x.LogType == type)
            .DeleteAsync()
            .GetAwaiter()
            .GetResult();

        if (deleted > 0)
        {
            RefreshLogChannelCacheInternal(gid, uow);
            return false;
        }

        if (cid is null)
        {
            RefreshLogChannelCacheInternal(gid, uow);
            return false;
        }

        uow.GetTable<LogChannel>()
            .InsertAsync(() => new LogChannel
            {
                GuildId = gid,
                LogType = type,
                ChannelId = cid.Value
            })
            .GetAwaiter()
            .GetResult();

        RefreshLogChannelCacheInternal(gid, uow);
        return true;
    }

    private void MuteCommands_UserMuted(
        IGuildUser usr,
        IUser mod,
        MuteType muteType,
        string reason)
        => _ = Task.Run(async () =>
        {
            try
            {
                if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserMuted, out _))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, LogType.UserMuted)) is null)
                    return;
                var mutes = string.Empty;
                var mutedLocalized = GetText(logChannel.Guild, strs.muted_sn);
                switch (muteType)
                {
                    case MuteType.Voice:
                        mutes = "🔇 " + GetText(logChannel.Guild, strs.xmuted_voice(mutedLocalized, mod.ToString()));
                        break;
                    case MuteType.Chat:
                        mutes = "🔇 " + GetText(logChannel.Guild, strs.xmuted_text(mutedLocalized, mod.ToString()));
                        break;
                    case MuteType.All:
                        mutes = "🔇 "
                                + GetText(logChannel.Guild, strs.xmuted_text_and_voice(mutedLocalized, mod.ToString()));
                        break;
                }

                var embed = _sender.CreateEmbed()
                    .WithAuthor(mutes)
                    .WithTitle($"{usr.Username}#{usr.Discriminator} | {usr.Id}")
                    .WithFooter(CurrentTime(usr.Guild))
                    .WithOkColor();

                if (!string.IsNullOrWhiteSpace(reason))
                    embed.WithDescription(reason);

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });

    private void MuteCommands_UserUnmuted(
        IGuildUser usr,
        IUser mod,
        MuteType muteType,
        string reason)
        => _ = Task.Run(async () =>
        {
            try
            {
                if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserMuted, out _))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, LogType.UserMuted)) is null)
                    return;

                var mutes = string.Empty;
                var unmutedLocalized = GetText(logChannel.Guild, strs.unmuted_sn);
                switch (muteType)
                {
                    case MuteType.Voice:
                        mutes = "🔊 " + GetText(logChannel.Guild, strs.xmuted_voice(unmutedLocalized, mod.ToString()));
                        break;
                    case MuteType.Chat:
                        mutes = "🔊 " + GetText(logChannel.Guild, strs.xmuted_text(unmutedLocalized, mod.ToString()));
                        break;
                    case MuteType.All:
                        mutes = "🔊 "
                                + GetText(logChannel.Guild,
                                    strs.xmuted_text_and_voice(unmutedLocalized, mod.ToString()));
                        break;
                }

                var embed = _sender.CreateEmbed()
                    .WithAuthor(mutes)
                    .WithTitle($"{usr.Username}#{usr.Discriminator} | {usr.Id}")
                    .WithFooter($"{CurrentTime(usr.Guild)}")
                    .WithOkColor();

                if (!string.IsNullOrWhiteSpace(reason))
                    embed.WithDescription(reason);

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });

    public Task TriggeredAntiProtection(PunishmentAction action, ProtectionType protection, params IGuildUser[] users)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (users.Length == 0)
                    return;

                var guildId = users.First().Guild.Id;
                if (!TryGetLogChannelId(guildId, LogType.Other, out _))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(users.First().Guild, LogType.Other)) is null)
                    return;

                var punishment = string.Empty;
                switch (action)
                {
                    case PunishmentAction.Mute:
                        punishment = "🔇 " + GetText(logChannel.Guild, strs.muted_pl).ToUpperInvariant();
                        break;
                    case PunishmentAction.Kick:
                        punishment = "👢 " + GetText(logChannel.Guild, strs.kicked_pl).ToUpperInvariant();
                        break;
                    case PunishmentAction.Softban:
                        punishment = "☣ " + GetText(logChannel.Guild, strs.soft_banned_pl).ToUpperInvariant();
                        break;
                    case PunishmentAction.Ban:
                        punishment = "⛔️ " + GetText(logChannel.Guild, strs.banned_pl).ToUpperInvariant();
                        break;
                    case PunishmentAction.RemoveRoles:
                        punishment = "⛔️ " + GetText(logChannel.Guild, strs.remove_roles_pl).ToUpperInvariant();
                        break;
                }

                var embed = _sender.CreateEmbed()
                    .WithAuthor($"🛡 Anti-{protection}")
                    .WithTitle(GetText(logChannel.Guild, strs.users) + " " + punishment)
                    .WithDescription(string.Join("\n", users.Select(u => u.ToString())))
                    .WithFooter(CurrentTime(logChannel.Guild))
                    .WithOkColor();

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private string GetRoleDeletedKey(ulong roleId)
        => $"role_deleted_{roleId}";

    private Task _client_RoleDeleted(SocketRole socketRole)
    {
        Serilog.Log.Information("Role deleted {RoleId}", socketRole.Id);
        _memoryCache.Set(GetRoleDeletedKey(socketRole.Id), true, TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    private bool IsRoleDeleted(ulong roleId)
    {
        var isDeleted = _memoryCache.TryGetValue(GetRoleDeletedKey(roleId), out _);
        return isDeleted;
    }

    private Task _client_GuildUserUpdated(Cacheable<SocketGuildUser, ulong> optBefore, SocketGuildUser after)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var before = await optBefore.GetOrDownloadAsync();

                if (before is null)
                    return;

                if (!TryGetLogChannelId(before.Guild.Id, LogType.UserUpdated, out _)
                    || IsUserIgnored(before.Guild.Id, after.Id))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(before.Guild, LogType.UserUpdated)) is null)
                    return;

                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithFooter(CurrentTime(before.Guild))
                    .WithTitle($"{before.Username}#{before.Discriminator} | {before.Id}");
                if (before.Nickname != after.Nickname)
                {
                    embed.WithAuthor("👥 " + GetText(logChannel.Guild, strs.nick_change))
                        .AddField(GetText(logChannel.Guild, strs.old_nick),
                            $"{before.Nickname}#{before.Discriminator}")
                        .AddField(GetText(logChannel.Guild, strs.new_nick),
                            $"{after.Nickname}#{after.Discriminator}");

                    await _sender.Response(logChannel).Embed(embed).SendAsync();
                }
                else if (!before.Roles.SequenceEqual(after.Roles))
                {
                    if (before.Roles.Count < after.Roles.Count)
                    {
                        var diffRoles = after.Roles.Where(r => !before.Roles.Contains(r)).Select(r => r.Name);
                        embed.WithAuthor("⚔ " + GetText(logChannel.Guild, strs.user_role_add))
                            .WithDescription(string.Join(", ", diffRoles).SanitizeMentions());

                        await _sender.Response(logChannel).Embed(embed).SendAsync();
                    }
                    else if (before.Roles.Count > after.Roles.Count)
                    {
                        await Task.Delay(1000);
                        var diffRoles = before.Roles.Where(r => !after.Roles.Contains(r) && !IsRoleDeleted(r.Id))
                            .Select(r => r.Name)
                            .ToList();

                        if (diffRoles.Any())
                        {
                            embed.WithAuthor("⚔ " + GetText(logChannel.Guild, strs.user_role_rem))
                                .WithDescription(string.Join(", ", diffRoles).SanitizeMentions());

                            await _sender.Response(logChannel).Embed(embed).SendAsync();
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_ChannelUpdated(IChannel cbefore, IChannel cafter)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (cbefore is not IGuildChannel before)
                    return;

                var after = (IGuildChannel)cafter;

                if (!TryGetLogChannelId(before.Guild.Id, LogType.ChannelUpdated, out _)
                    || IsChannelIgnored(before.Guild.Id, after.Id, (after as INestedChannel)?.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(before.Guild, LogType.ChannelUpdated)) is null)
                    return;

                var embed = _sender.CreateEmbed().WithOkColor().WithFooter(CurrentTime(before.Guild));

                var beforeTextChannel = cbefore as ITextChannel;
                var afterTextChannel = cafter as ITextChannel;

                if (before.Name != after.Name)
                {
                    embed.WithTitle("ℹ️ " + GetText(logChannel.Guild, strs.ch_name_change))
                        .WithDescription($"{after} | {after.Id}")
                        .AddField(GetText(logChannel.Guild, strs.ch_old_name), before.Name);
                }
                else if (beforeTextChannel?.Topic != afterTextChannel?.Topic)
                {
                    embed.WithTitle("ℹ️ " + GetText(logChannel.Guild, strs.ch_topic_change))
                        .WithDescription($"{after} | {after.Id}")
                        .AddField(GetText(logChannel.Guild, strs.old_topic), beforeTextChannel?.Topic ?? "-")
                        .AddField(GetText(logChannel.Guild, strs.new_topic), afterTextChannel?.Topic ?? "-");
                }
                else
                    return;

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_ChannelDestroyed(IChannel ich)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (ich is not IGuildChannel ch)
                    return;

                if (!TryGetLogChannelId(ch.Guild.Id, LogType.ChannelDestroyed, out _)
                    || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch as INestedChannel)?.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, LogType.ChannelDestroyed)) is null)
                    return;

                string title;
                if (ch is IVoiceChannel)
                    title = GetText(logChannel.Guild, strs.voice_chan_destroyed);
                else
                    title = GetText(logChannel.Guild, strs.text_chan_destroyed);

                await _sender.Response(logChannel).Embed(_sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("🆕 " + title)
                    .WithDescription($"{ch.Name} | {ch.Id}")
                    .WithFooter(CurrentTime(ch.Guild))).SendAsync();
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_ChannelCreated(IChannel ich)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (ich is not IGuildChannel ch)
                    return;

                if (!TryGetLogChannelId(ch.Guild.Id, LogType.ChannelCreated, out _)
                    || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch as INestedChannel)?.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, LogType.ChannelCreated)) is null)
                    return;
                string title;
                if (ch is IVoiceChannel)
                    title = GetText(logChannel.Guild, strs.voice_chan_created);
                else
                    title = GetText(logChannel.Guild, strs.text_chan_created);

                await _sender.Response(logChannel).Embed(_sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("🆕 " + title)
                    .WithDescription($"{ch.Name} | {ch.Id}")
                    .WithFooter(CurrentTime(ch.Guild))).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_UserVoiceStateUpdated(SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (iusr is not IGuildUser usr || usr.IsBot)
                    return;

                var beforeVch = before.VoiceChannel;
                var afterVch = after.VoiceChannel;

                if (afterVch is not null)
                {
                    var serverMuteChanged = before.IsMuted != after.IsMuted;
                    var serverDeafChanged = before.IsDeafened != after.IsDeafened;

                    if (serverMuteChanged || serverDeafChanged)
                        await LogServerVoiceMuteDeafen(usr, before, after, serverMuteChanged, serverDeafChanged);
                }

                if (beforeVch == afterVch)
                    return;

                if (!TryGetLogChannelId(usr.Guild.Id, LogType.VoicePresence, out _)
                    || IsUserIgnored(usr.Guild.Id, iusr.Id))
                    return;

                var vcId = afterVch?.Id ?? beforeVch?.Id ?? 0;
                var vcCategoryId = (afterVch as INestedChannel)?.CategoryId
                                   ?? (beforeVch as INestedChannel)?.CategoryId;
                if (vcId != 0 && IsChannelIgnored(usr.Guild.Id, vcId, vcCategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, LogType.VoicePresence)) is null)
                    return;

                var str = string.Empty;
                if (beforeVch?.Guild == afterVch?.Guild)
                {
                    str = "🎙"
                          + Format.Code(PrettyCurrentTime(usr.Guild))
                          + GetText(logChannel.Guild,
                              strs.user_vmoved("👤" + Format.Bold(usr.Username),
                                  Format.Bold(beforeVch?.Name ?? ""),
                                  Format.Bold(afterVch?.Name ?? "")));
                }
                else if (beforeVch is null)
                {
                    str = "🎙"
                          + Format.Code(PrettyCurrentTime(usr.Guild))
                          + GetText(logChannel.Guild,
                              strs.user_vjoined("👤" + Format.Bold(usr.Username),
                                  Format.Bold(afterVch?.Name ?? "")));
                }
                else if (afterVch is null)
                {
                    str = "🎙"
                          + Format.Code(PrettyCurrentTime(usr.Guild))
                          + GetText(logChannel.Guild,
                              strs.user_vleft("👤" + Format.Bold(usr.Username),
                                  Format.Bold(beforeVch.Name ?? "")));
                }

                if (!string.IsNullOrWhiteSpace(str))
                {
                    PresenceUpdates.GetOrAdd(logChannel, _ => new System.Collections.Concurrent.ConcurrentQueue<string>()).Enqueue(str);
                }
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private async Task LogServerVoiceMuteDeafen(
        IGuildUser usr,
        SocketVoiceState before,
        SocketVoiceState after,
        bool serverMuteChanged,
        bool serverDeafChanged)
    {
        if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserMuted, out _)
            || IsUserIgnored(usr.Guild.Id, usr.Id))
            return;

        ITextChannel? logChannel;
        if ((logChannel = await TryGetLogChannel(usr.Guild, LogType.UserMuted)) is null)
            return;

        var modName = "Unknown";
        try
        {
            var auditLogs = await usr.Guild.GetAuditLogsAsync(5, actionType: ActionType.MemberUpdated);
            var entry = auditLogs
                .Where(e => e.Data is SocketMemberUpdateAuditLogData data && data.Target.Id == usr.Id)
                .Where(e => e.CreatedAt > DateTimeOffset.UtcNow.AddSeconds(-5))
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();

            if (entry is not null)
            {
                if (entry.User.Id == _client.CurrentUser.Id)
                    return;

                modName = entry.User.ToString() ?? "Unknown";
            }
        }
        catch
        {
        }

        var changes = new List<string>();

        if (serverMuteChanged)
        {
            changes.Add(after.IsMuted
                ? GetText(logChannel.Guild, strs.voice_server_muted)
                : GetText(logChannel.Guild, strs.voice_server_unmuted));
        }

        if (serverDeafChanged)
        {
            changes.Add(after.IsDeafened
                ? GetText(logChannel.Guild, strs.voice_server_deafened)
                : GetText(logChannel.Guild, strs.voice_server_undeafened));
        }

        var emoji = after.IsMuted || after.IsDeafened ? "🔇" : "🔊";
        var description = string.Join("\n", changes);

        var embed = _sender.CreateEmbed()
            .WithAuthor($"{emoji} {description}")
            .WithTitle($"{usr.Username} | {usr.Id}")
            .AddField("Moderator", modName, true)
            .WithFooter(CurrentTime(usr.Guild))
            .WithOkColor();

        await _sender.Response(logChannel).Embed(embed).SendAsync();
    }

    private Task _client_UserLeft(SocketGuild guild, SocketUser usr)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!TryGetLogChannelId(guild.Id, LogType.UserLeft, out _)
                    || IsUserIgnored(guild.Id, usr.Id))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(guild, LogType.UserLeft)) is null)
                    return;
                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("❌ " + GetText(logChannel.Guild, strs.user_left))
                    .WithDescription(usr.ToString())
                    .AddField("Id", usr.Id.ToString())
                    .WithFooter(CurrentTime(guild));

                if (Uri.IsWellFormedUriString(usr.GetAvatarUrl(), UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_UserJoined(IGuildUser usr)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserJoined, out _))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, LogType.UserJoined)) is null)
                    return;

                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("✅ " + GetText(logChannel.Guild, strs.user_joined))
                    .WithDescription($"{usr.Mention} `{usr}`")
                    .AddField("Id", usr.Id.ToString())
                    .AddField(GetText(logChannel.Guild, strs.joined_server),
                        $"{usr.JoinedAt?.ToString("dd.MM.yyyy HH:mm") ?? "?"}",
                        true)
                    .AddField(GetText(logChannel.Guild, strs.joined_discord),
                        $"{usr.CreatedAt:dd.MM.yyyy HH:mm}",
                        true)
                    .WithFooter(CurrentTime(usr.Guild));

                if (Uri.IsWellFormedUriString(usr.GetAvatarUrl(), UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_UserUnbanned(IUser usr, IGuild guild)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_ignoreUnbanIds.TryRemove((guild.Id, usr.Id)))
                    return;

                if (!TryGetLogChannelId(guild.Id, LogType.UserUnbanned, out _)
                    || IsUserIgnored(guild.Id, usr.Id))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(guild, LogType.UserUnbanned)) is null)
                    return;
                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("♻️ " + GetText(logChannel.Guild, strs.user_unbanned))
                    .WithDescription(usr.ToString()!)
                    .AddField("Id", usr.Id.ToString())
                    .WithFooter(CurrentTime(guild));

                if (Uri.IsWellFormedUriString(usr.GetAvatarUrl(), UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_UserBanned(IUser usr, IGuild guild)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_ignoreBanIds.TryRemove((guild.Id, usr.Id)))
                    return;

                if (!TryGetLogChannelId(guild.Id, LogType.UserBanned, out _)
                    || IsUserIgnored(guild.Id, usr.Id))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(guild, LogType.UserBanned)) == null)
                    return;


                string? reason = null;
                try
                {
                    var ban = await guild.GetBanAsync(usr);
                    reason = ban?.Reason;
                }
                catch
                {
                }

                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("🚫 " + GetText(logChannel.Guild, strs.user_banned))
                    .WithDescription(usr.ToString()!)
                    .AddField("Id", usr.Id.ToString())
                    .AddField("Reason", string.IsNullOrWhiteSpace(reason) ? "-" : reason)
                    .WithFooter(CurrentTime(guild));

                var avatarUrl = usr.GetAvatarUrl();

                if (Uri.IsWellFormedUriString(avatarUrl, UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_MessageDeleted(Cacheable<IMessage, ulong> optMsg, Cacheable<IMessageChannel, ulong> optCh)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (optMsg.Value is not IUserMessage msg || msg.IsAuthor(_client))
                    return;

                if (_ignoreMessageIds.Contains(msg.Id))
                    return;

                var ch = optCh.Value;
                if (ch is not ITextChannel channel)
                    return;

                if (!TryGetLogChannelId(channel.Guild.Id, LogType.MessageDeleted, out _)
                    || IsChannelIgnored(channel.Guild.Id, channel.Id, channel.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(channel.Guild, LogType.MessageDeleted)) is null
                    || logChannel.Id == msg.Id)
                    return;

                var resolvedMessage = msg.Resolve(TagHandling.FullName);
                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("🗑 "
                               + GetText(logChannel.Guild, strs.msg_del(((ITextChannel)msg.Channel).Name)))
                    .WithDescription(msg.Author.ToString()!)
                    .AddField(GetText(logChannel.Guild, strs.content),
                        string.IsNullOrWhiteSpace(resolvedMessage) ? "-" : resolvedMessage)
                    .AddField("Id", msg.Id.ToString())
                    .WithFooter(CurrentTime(channel.Guild));
                if (msg.Attachments.Any())
                {
                    embed.AddField(GetText(logChannel.Guild, strs.attachments),
                        string.Join(", ", msg.Attachments.Select(a => a.Url)));
                }

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task _client_MessageUpdated(
        Cacheable<IMessage, ulong> optmsg,
        SocketMessage imsg2,
        ISocketMessageChannel ch)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (imsg2 is not IUserMessage after || after.IsAuthor(_client))
                    return;

                if ((optmsg.HasValue ? optmsg.Value : null) is not IUserMessage before)
                    return;

                if (ch is not ITextChannel channel)
                    return;

                if (before.Content == after.Content)
                    return;

                if (before.Author.IsBot)
                    return;

                if (!TryGetLogChannelId(channel.Guild.Id, LogType.MessageUpdated, out _)
                    || IsChannelIgnored(channel.Guild.Id, channel.Id, channel.CategoryId))
                    return;

                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(channel.Guild, LogType.MessageUpdated)) is null
                    || logChannel.Id == after.Channel.Id)
                    return;

                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithTitle("📝 "
                               + GetText(logChannel.Guild,
                                   strs.msg_update(((ITextChannel)after.Channel).Name)))
                    .WithDescription(after.Author.ToString()!)
                    .AddField(GetText(logChannel.Guild, strs.old_msg),
                        string.IsNullOrWhiteSpace(before.Content)
                            ? "-"
                            : before.Resolve(TagHandling.FullName))
                    .AddField(GetText(logChannel.Guild, strs.new_msg),
                        string.IsNullOrWhiteSpace(after.Content) ? "-" : after.Resolve(TagHandling.FullName))
                    .AddField("Id", after.Id.ToString())
                    .WithFooter(CurrentTime(channel.Guild));

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }
}
