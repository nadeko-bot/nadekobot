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
    private ConcurrentDictionary<ulong, GuildIgnores> _logIgnores = new();

    private ConcurrentDictionary<ITextChannel, System.Collections.Concurrent.ConcurrentQueue<string>> PresenceUpdates { get; } = new();
    private readonly DiscordSocketClient _client;

    private readonly IBotStrings _strings;
    private readonly DbService _db;
    private readonly MuteService _mute;
    private readonly ProtectionService _prot;
    private readonly GuildTimezoneService _tz;
    private readonly IMemoryCache _memoryCache;

    private readonly ConcurrentDictionary<(ulong GuildId, LogType Type), byte> _unsetInFlight = new();
    private readonly ConcurrentHashSet<ulong> _ignoreMessageIds = [];
    private readonly ConcurrentHashSet<(ulong GuildId, ulong UserId)> _ignoreBanIds = [];
    private readonly ConcurrentHashSet<(ulong GuildId, ulong UserId)> _ignoreUnbanIds = [];
    private readonly UserPunishService _punishService;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;

    public LogCommandService(
        DiscordSocketClient client,
        IBotStrings strings,
        DbService db,
        MuteService mute,
        ProtectionService prot,
        GuildTimezoneService tz,
        IMemoryCache memoryCache,
        UserPunishService punishService,
        IMessageSenderService sender,
        ShardData shardData)
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
        _shardData = shardData;

        _client.MessageUpdated += HandleMessageUpdated;
        _client.MessageDeleted += HandleMessageDeleted;
        _client.UserBanned += HandleUserBanned;
        _client.UserUnbanned += HandleUserUnbanned;
        _client.UserJoined += HandleUserJoined;
        _client.UserLeft += HandleUserLeft;
        _client.UserVoiceStateUpdated += HandleUserVoiceStateUpdated;
        _client.GuildMemberUpdated += HandleGuildUserUpdated;
        _client.PresenceUpdated += HandlePresenceUpdated;
        _client.UserUpdated += HandleUserUpdated;
        _client.ChannelCreated += HandleChannelCreated;
        _client.ChannelDestroyed += HandleChannelDestroyed;
        _client.ChannelUpdated += HandleChannelUpdated;
        _client.RoleDeleted += HandleRoleDeleted;
        _client.ThreadCreated += HandleThreadCreated;
        _client.ThreadDeleted += HandleThreadDeleted;

        _mute.UserMuted += HandleUserMuted;
        _mute.UserUnmuted += HandleUserUnmuted;
        _prot.OnAntiProtectionTriggered += HandleAntiProtectionTriggered;
        _punishService.OnUserWarned += HandleUserWarned;
    }

    #region Cache helpers

    private bool TryGetLogChannelId(ulong guildId, LogType logType, out ulong channelId)
    {
        channelId = 0;
        return _logChannels.TryGetValue(guildId, out var dict) && dict.TryGetValue(logType, out channelId);
    }

    private bool IsUserIgnored(ulong guildId, ulong userId)
        => _logIgnores.TryGetValue(guildId, out var g) && g.Users.Contains(userId);

    private bool IsChannelIgnored(ulong guildId, ulong channelId, ulong? categoryId)
    {
        if (!_logIgnores.TryGetValue(guildId, out var g))
            return false;

        if (g.Channels.Contains(channelId))
            return true;

        return categoryId is { } cat && g.Categories.Contains(cat);
    }

    private async Task<ITextChannel?> TryGetLogChannel(IGuild guild, LogType logType)
    {
        if (!TryGetLogChannelId(guild.Id, logType, out var channelId))
            return null;

        var channel = await guild.GetTextChannelAsync(channelId);

        if (channel is null)
        {
            if (_unsetInFlight.TryAdd((guild.Id, logType), 0))
            {
                try { await UnsetLogChannelInternalAsync(guild.Id, logType); }
                finally { _unsetInFlight.TryRemove((guild.Id, logType), out _); }
            }

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
            _logIgnores[guildId] = new GuildIgnores(ignores);
    }

    #endregion

    #region Event filters (gateway thread -- cheap checks only)

    private Task HandlePresenceUpdated(SocketUser user, SocketPresence? before, SocketPresence? after)
    {
        if (user is not SocketGuildUser gu
            || before is null
            || after is null
            || !TryGetLogChannelId(gu.Guild.Id, LogType.UserPresence, out _)
            || user.IsBot
            || IsUserIgnored(gu.Guild.Id, gu.Id))
            return Task.CompletedTask;

        _ = OnPresenceUpdatedAsync(gu, before, after);
        return Task.CompletedTask;
    }

    private Task HandleThreadDeleted(Cacheable<SocketThreadChannel, ulong> sch)
    {
        if (!sch.HasValue)
            return Task.CompletedTask;

        var ch = sch.Value;
        if (!TryGetLogChannelId(ch.Guild.Id, LogType.ThreadDeleted, out _)
            || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch.ParentChannel as INestedChannel)?.CategoryId))
            return Task.CompletedTask;

        _ = OnThreadDeletedAsync(ch);
        return Task.CompletedTask;
    }

    private Task HandleThreadCreated(SocketThreadChannel ch)
    {
        if (!TryGetLogChannelId(ch.Guild.Id, LogType.ThreadCreated, out _)
            || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch.ParentChannel as INestedChannel)?.CategoryId))
            return Task.CompletedTask;

        _ = OnThreadCreatedAsync(ch);
        return Task.CompletedTask;
    }

    private Task HandleUserUpdated(SocketUser before, SocketUser uAfter)
    {
        if (uAfter is not SocketGuildUser after)
            return Task.CompletedTask;

        if (IsUserIgnored(after.Guild.Id, after.Id))
            return Task.CompletedTask;

        if (before.Username != after.Username
            && TryGetLogChannelId(after.Guild.Id, LogType.UsernameUpdated, out _))
        {
            _ = OnUsernameUpdatedAsync(before, after);
            return Task.CompletedTask;
        }

        if (before.AvatarId != after.AvatarId
            && TryGetLogChannelId(after.Guild.Id, LogType.AvatarUpdated, out _))
        {
            _ = OnAvatarUpdatedAsync(before, after);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private Task HandleGuildUserUpdated(Cacheable<SocketGuildUser, ulong> optBefore, SocketGuildUser after)
    {
        if (IsUserIgnored(after.Guild.Id, after.Id))
            return Task.CompletedTask;

        var wantNick = TryGetLogChannelId(after.Guild.Id, LogType.NicknameUpdated, out _);
        var wantRoles = TryGetLogChannelId(after.Guild.Id, LogType.RolesUpdated, out _);
        if (!wantNick && !wantRoles)
            return Task.CompletedTask;

        _ = OnGuildUserUpdatedAsync(optBefore, after);
        return Task.CompletedTask;
    }

    private Task HandleChannelUpdated(IChannel cbefore, IChannel cafter)
    {
        if (cbefore is not IGuildChannel before)
            return Task.CompletedTask;

        var after = (IGuildChannel)cafter;
        if (!TryGetLogChannelId(before.Guild.Id, LogType.ChannelUpdated, out _)
            || IsChannelIgnored(before.Guild.Id, after.Id, (after as INestedChannel)?.CategoryId))
            return Task.CompletedTask;

        _ = OnChannelUpdatedAsync(before, after, cbefore, cafter);
        return Task.CompletedTask;
    }

    private Task HandleChannelDestroyed(IChannel ich)
    {
        if (ich is not IGuildChannel ch
            || !TryGetLogChannelId(ch.Guild.Id, LogType.ChannelDestroyed, out _)
            || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch as INestedChannel)?.CategoryId))
            return Task.CompletedTask;

        _ = OnChannelDestroyedAsync(ch);
        return Task.CompletedTask;
    }

    private Task HandleChannelCreated(IChannel ich)
    {
        if (ich is not IGuildChannel ch
            || !TryGetLogChannelId(ch.Guild.Id, LogType.ChannelCreated, out _)
            || IsChannelIgnored(ch.Guild.Id, ch.Id, (ch as INestedChannel)?.CategoryId))
            return Task.CompletedTask;

        _ = OnChannelCreatedAsync(ch);
        return Task.CompletedTask;
    }

    private Task HandleUserVoiceStateUpdated(SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        if (iusr is not IGuildUser usr || usr.IsBot)
            return Task.CompletedTask;

        var serverStateChanged = before.IsMuted != after.IsMuted || before.IsDeafened != after.IsDeafened;
        var channelChanged = before.VoiceChannel != after.VoiceChannel;

        if (!serverStateChanged && !channelChanged)
            return Task.CompletedTask;

        var wantMuted = serverStateChanged && TryGetLogChannelId(usr.Guild.Id, LogType.UserMuted, out _);
        var wantVoice = channelChanged && TryGetLogChannelId(usr.Guild.Id, LogType.VoicePresence, out _);

        if (!wantMuted && !wantVoice)
            return Task.CompletedTask;

        _ = OnUserVoiceStateUpdatedAsync(usr, iusr, before, after);
        return Task.CompletedTask;
    }

    private Task HandleUserLeft(SocketGuild guild, SocketUser usr)
    {
        if (!TryGetLogChannelId(guild.Id, LogType.UserLeft, out _)
            || IsUserIgnored(guild.Id, usr.Id))
            return Task.CompletedTask;

        _ = OnUserLeftAsync(guild, usr);
        return Task.CompletedTask;
    }

    private Task HandleUserJoined(IGuildUser usr)
    {
        if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserJoined, out _))
            return Task.CompletedTask;

        _ = OnUserJoinedAsync(usr);
        return Task.CompletedTask;
    }

    private Task HandleUserUnbanned(IUser usr, IGuild guild)
    {
        if (_ignoreUnbanIds.TryRemove((guild.Id, usr.Id)))
            return Task.CompletedTask;

        if (!TryGetLogChannelId(guild.Id, LogType.UserUnbanned, out _)
            || IsUserIgnored(guild.Id, usr.Id))
            return Task.CompletedTask;

        _ = OnUserUnbannedAsync(usr, guild);
        return Task.CompletedTask;
    }

    private Task HandleUserBanned(IUser usr, IGuild guild)
    {
        if (_ignoreBanIds.TryRemove((guild.Id, usr.Id)))
            return Task.CompletedTask;

        if (!TryGetLogChannelId(guild.Id, LogType.UserBanned, out _)
            || IsUserIgnored(guild.Id, usr.Id))
            return Task.CompletedTask;

        _ = OnUserBannedAsync(usr, guild);
        return Task.CompletedTask;
    }

    private Task HandleMessageDeleted(Cacheable<IMessage, ulong> optMsg, Cacheable<IMessageChannel, ulong> optCh)
    {
        if (optMsg.Value is not IUserMessage msg || msg.IsAuthor(_client))
            return Task.CompletedTask;

        if (_ignoreMessageIds.Contains(msg.Id))
            return Task.CompletedTask;

        if (optCh.Value is not ITextChannel channel)
            return Task.CompletedTask;

        if (!TryGetLogChannelId(channel.Guild.Id, LogType.MessageDeleted, out _)
            || IsChannelIgnored(channel.Guild.Id, channel.Id, channel.CategoryId))
            return Task.CompletedTask;

        _ = OnMessageDeletedAsync(msg, channel);
        return Task.CompletedTask;
    }

    private Task HandleMessageUpdated(Cacheable<IMessage, ulong> optmsg, SocketMessage imsg2, ISocketMessageChannel ch)
    {
        if (imsg2 is not IUserMessage after || after.IsAuthor(_client))
            return Task.CompletedTask;

        if ((optmsg.HasValue ? optmsg.Value : null) is not IUserMessage before)
            return Task.CompletedTask;

        if (ch is not ITextChannel channel || before.Content == after.Content || before.Author.IsBot)
            return Task.CompletedTask;

        if (!TryGetLogChannelId(channel.Guild.Id, LogType.MessageUpdated, out _)
            || IsChannelIgnored(channel.Guild.Id, channel.Id, channel.CategoryId))
            return Task.CompletedTask;

        _ = OnMessageUpdatedAsync(before, after, channel);
        return Task.CompletedTask;
    }

    private Task HandleRoleDeleted(SocketRole socketRole)
    {
        Serilog.Log.Information("Role deleted {RoleId}", socketRole.Id);
        _memoryCache.Set(GetRoleDeletedKey(socketRole.Id), true, TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    private void HandleUserMuted(IGuildUser usr, IUser mod, MuteType muteType, string reason)
    {
        if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserMuted, out _))
            return;

        _ = OnUserMutedAsync(usr, mod, muteType, reason);
    }

    private void HandleUserUnmuted(IGuildUser usr, IUser mod, MuteType muteType, string reason)
    {
        if (!TryGetLogChannelId(usr.Guild.Id, LogType.UserMuted, out _))
            return;

        _ = OnUserUnmutedAsync(usr, mod, muteType, reason);
    }

    private Task HandleAntiProtectionTriggered(PunishmentAction action, ProtectionType protection, params IGuildUser[] users)
    {
        if (users.Length == 0)
            return Task.CompletedTask;

        var guildId = users[0].Guild.Id;
        if (!TryGetLogChannelId(guildId, LogType.Other, out _))
            return Task.CompletedTask;

        _ = OnAntiProtectionTriggeredAsync(action, protection, users);
        return Task.CompletedTask;
    }

    private async Task HandleUserWarned(Warning arg)
    {
        if (!TryGetLogChannelId(arg.GuildId, LogType.UserWarned, out _))
            return;

        try
        {
            var g = _client.GetGuild(arg.GuildId);

            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(g, LogType.UserWarned)) is null)
                return;

            var embed = _sender.CreateEmbed()
                .WithOkColor()
                .WithTitle("⚠️ User Warned")
                .WithDescription($"<@{arg.UserId}> | {arg.UserId}")
                .AddField("Mod", arg.Moderator)
                .AddField("Reason", string.IsNullOrWhiteSpace(arg.Reason) ? "-" : arg.Reason, true)
                .WithFooter(CurrentTime(g));

            await _sender.Response(logChannel).Embed(embed).SendAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserWarned log event");
        }
    }

    #endregion

    #region Async handlers (thread pool -- expensive work)

    private async Task OnPresenceUpdatedAsync(SocketGuildUser gu, SocketPresence before, SocketPresence after)
    {
        try
        {
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(gu.Guild, LogType.UserPresence)) is null)
                return;

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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling PresenceUpdated log event");
        }
    }

    private async Task OnThreadDeletedAsync(SocketThreadChannel ch)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling ThreadDeleted log event");
        }
    }

    private async Task OnThreadCreatedAsync(SocketThreadChannel ch)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling ThreadCreated log event");
        }
    }

    private async Task OnUsernameUpdatedAsync(SocketUser before, SocketGuildUser after)
    {
        try
        {
            var g = after.Guild;
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(g, LogType.UsernameUpdated)) is null)
                return;

            var embed = _sender.CreateEmbed()
                .WithTitle("👥 " + GetText(g, strs.username_changed))
                .WithDescription($"{before.Username} | {before.Id}")
                .AddField("Old Name", $"{before.Username}", true)
                .AddField("New Name", $"{after.Username}", true)
                .WithFooter(CurrentTime(g))
                .WithOkColor();

            await _sender.Response(logChannel).Embed(embed).SendAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UsernameUpdated log event");
        }
    }

    private async Task OnAvatarUpdatedAsync(SocketUser before, SocketGuildUser after)
    {
        try
        {
            var g = after.Guild;
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(g, LogType.AvatarUpdated)) is null)
                return;

            var embed = _sender.CreateEmbed()
                .WithTitle("👥" + GetText(g, strs.avatar_changed))
                .WithDescription($"{before.Username}#{before.Discriminator} | {before.Id}")
                .WithFooter(CurrentTime(g))
                .WithOkColor();

            var bav = before.RealAvatarUrl();
            if (bav.IsAbsoluteUri)
                embed.WithThumbnailUrl(bav.ToString());

            var aav = after.RealAvatarUrl();
            if (aav.IsAbsoluteUri)
                embed.WithImageUrl(aav.ToString());

            await _sender.Response(logChannel).Embed(embed).SendAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling AvatarUpdated log event");
        }
    }

    private async Task OnGuildUserUpdatedAsync(Cacheable<SocketGuildUser, ulong> optBefore, SocketGuildUser after)
    {
        try
        {
            var before = await optBefore.GetOrDownloadAsync();
            if (before is null)
                return;

            if (before.Nickname != after.Nickname
                && TryGetLogChannelId(before.Guild.Id, LogType.NicknameUpdated, out _))
            {
                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(before.Guild, LogType.NicknameUpdated)) is null)
                    return;

                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithFooter(CurrentTime(before.Guild))
                    .WithTitle($"{before.Username}#{before.Discriminator} | {before.Id}")
                    .WithAuthor("👥 " + GetText(logChannel.Guild, strs.nick_change))
                    .AddField(GetText(logChannel.Guild, strs.old_nick),
                        $"{before.Nickname}#{before.Discriminator}")
                    .AddField(GetText(logChannel.Guild, strs.new_nick),
                        $"{after.Nickname}#{after.Discriminator}");

                await _sender.Response(logChannel).Embed(embed).SendAsync();
            }
            else if (!before.Roles.SequenceEqual(after.Roles)
                     && TryGetLogChannelId(before.Guild.Id, LogType.RolesUpdated, out _))
            {
                ITextChannel? logChannel;
                if ((logChannel = await TryGetLogChannel(before.Guild, LogType.RolesUpdated)) is null)
                    return;

                var embed = _sender.CreateEmbed()
                    .WithOkColor()
                    .WithFooter(CurrentTime(before.Guild))
                    .WithTitle($"{before.Username}#{before.Discriminator} | {before.Id}");

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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling GuildUserUpdated log event");
        }
    }

    private async Task OnChannelUpdatedAsync(IGuildChannel before, IGuildChannel after, IChannel cbefore, IChannel cafter)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling ChannelUpdated log event");
        }
    }

    private async Task OnChannelDestroyedAsync(IGuildChannel ch)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling ChannelDestroyed log event");
        }
    }

    private async Task OnChannelCreatedAsync(IGuildChannel ch)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling ChannelCreated log event");
        }
    }

    private async Task OnUserVoiceStateUpdatedAsync(IGuildUser usr, SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        try
        {
            var beforeVch = before.VoiceChannel;
            var afterVch = after.VoiceChannel;

            if (afterVch is not null)
            {
                var serverMuteChanged = before.IsMuted != after.IsMuted;
                var serverDeafChanged = before.IsDeafened != after.IsDeafened;

                if (serverMuteChanged || serverDeafChanged)
                    await LogServerVoiceMuteDeafenInternalAsync(usr, before, after, serverMuteChanged, serverDeafChanged);
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserVoiceStateUpdated log event");
        }
    }

    private async Task LogServerVoiceMuteDeafenInternalAsync(
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

    private async Task OnUserLeftAsync(SocketGuild guild, SocketUser usr)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserLeft log event");
        }
    }

    private async Task OnUserJoinedAsync(IGuildUser usr)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserJoined log event");
        }
    }

    private async Task OnUserUnbannedAsync(IUser usr, IGuild guild)
    {
        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserUnbanned log event");
        }
    }

    private async Task OnUserBannedAsync(IUser usr, IGuild guild)
    {
        try
        {
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(guild, LogType.UserBanned)) is null)
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
                embed.WithThumbnailUrl(avatarUrl);

            await _sender.Response(logChannel).Embed(embed).SendAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserBanned log event");
        }
    }

    private async Task OnMessageDeletedAsync(IUserMessage msg, ITextChannel channel)
    {
        try
        {
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(channel.Guild, LogType.MessageDeleted)) is null
                || logChannel.Id == msg.Id)
                return;

            var resolvedMessage = msg.Resolve(TagHandling.FullName);
            var embed = _sender.CreateEmbed()
                .WithOkColor()
                .WithTitle("🗑 " + GetText(logChannel.Guild, strs.msg_del(((ITextChannel)msg.Channel).Name)))
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling MessageDeleted log event");
        }
    }

    private async Task OnMessageUpdatedAsync(IUserMessage before, IUserMessage after, ITextChannel channel)
    {
        try
        {
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(channel.Guild, LogType.MessageUpdated)) is null
                || logChannel.Id == after.Channel.Id)
                return;

            var embed = _sender.CreateEmbed()
                .WithOkColor()
                .WithTitle("📝 " + GetText(logChannel.Guild, strs.msg_update(((ITextChannel)after.Channel).Name)))
                .WithDescription(after.Author.ToString()!)
                .AddField(GetText(logChannel.Guild, strs.old_msg),
                    string.IsNullOrWhiteSpace(before.Content) ? "-" : before.Resolve(TagHandling.FullName))
                .AddField(GetText(logChannel.Guild, strs.new_msg),
                    string.IsNullOrWhiteSpace(after.Content) ? "-" : after.Resolve(TagHandling.FullName))
                .AddField("Id", after.Id.ToString())
                .WithFooter(CurrentTime(channel.Guild));

            await _sender.Response(logChannel).Embed(embed).SendAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling MessageUpdated log event");
        }
    }

    private async Task OnUserMutedAsync(IGuildUser usr, IUser mod, MuteType muteType, string reason)
    {
        try
        {
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
                    mutes = "🔇 " + GetText(logChannel.Guild, strs.xmuted_text_and_voice(mutedLocalized, mod.ToString()));
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserMuted log event");
        }
    }

    private async Task OnUserUnmutedAsync(IGuildUser usr, IUser mod, MuteType muteType, string reason)
    {
        try
        {
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
                    mutes = "🔊 " + GetText(logChannel.Guild, strs.xmuted_text_and_voice(unmutedLocalized, mod.ToString()));
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling UserUnmuted log event");
        }
    }

    private async Task OnAntiProtectionTriggeredAsync(PunishmentAction action, ProtectionType protection, IGuildUser[] users)
    {
        try
        {
            ITextChannel? logChannel;
            if ((logChannel = await TryGetLogChannel(users[0].Guild, LogType.Other)) is null)
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error handling AntiProtection log event");
        }
    }

    #endregion

    #region Background tasks

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            var channels = await uow.GetTable<LogChannel>()
                .Where(Queries.GuildOnShard<LogChannel>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                .ToListAsyncLinqToDB();

            _logChannels = channels
                .GroupBy(x => x.GuildId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToFrozenDictionary(x => x.LogType, x => x.ChannelId))
                .ToConcurrent();

            var ignores = await uow.GetTable<LogIgnore>()
                .Where(Queries.GuildOnShard<LogIgnore>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                .ToListAsyncLinqToDB();

            _logIgnores = ignores
                .GroupBy(x => x.GuildId)
                .ToDictionary(
                    g => g.Key,
                    g => new GuildIgnores(g.ToList()))
                .ToConcurrent();
        }

        await Task.WhenAll(PresenceUpdateTask(), IgnoreMessageIdsClearTask());
    }

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

    #endregion

    #region Public API

    public IReadOnlyList<LogIgnore> GetLogIgnores(ulong guildId)
        => _logIgnores.TryGetValue(guildId, out var g) ? g.All : [];

    public ulong? GetLogChannelId(ulong guildId, LogType logType)
        => TryGetLogChannelId(guildId, logType, out var id) ? id : null;

    public void AddDeleteIgnore(ulong messageId)
        => _ignoreMessageIds.Add(messageId);

    public void AddBanIgnore(ulong guildId, ulong userId)
        => _ignoreBanIds.Add((guildId, userId));

    public void AddUnbanIgnore(ulong guildId, ulong userId)
        => _ignoreUnbanIds.Add((guildId, userId));

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

    #endregion

    #region Utilities

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

    private string GetRoleDeletedKey(ulong roleId)
        => $"role_deleted_{roleId}";

    private bool IsRoleDeleted(ulong roleId)
        => _memoryCache.TryGetValue(GetRoleDeletedKey(roleId), out _);

    #endregion
}
