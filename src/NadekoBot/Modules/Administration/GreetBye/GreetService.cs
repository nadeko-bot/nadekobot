using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using LinqToDB.Tools;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Services;

public readonly record struct GreetItem(GreetType Type, IUser User);

public class GreetService : INService, IReadyExecutor
{
    private static readonly TimeSpan _sendInterval = TimeSpan.FromMilliseconds(2016);

    private readonly DbService _db;

    private ConcurrentDictionary<GreetType, ConcurrentHashSet<ulong>> _enabled = new();

    private readonly DiscordSocketClient _client;

    private readonly IReplacementService _repSvc;
    private readonly IBotCache _cache;
    private readonly IMessageSenderService _sender;

    private readonly GreetQueue<GreetItem> _greetQueue = new();

    public GreetService(
        DiscordSocketClient client,
        DbService db,
        IMessageSenderService sender,
        IReplacementService repSvc,
        IBotCache cache
    )
    {
        _db = db;
        _client = client;
        _repSvc = repSvc;
        _cache = cache;
        _sender = sender;


        foreach (var type in Enum.GetValues<GreetType>())
        {
            _enabled[type] = new();
        }
    }

    public async Task OnReadyAsync()
    {
        // cache all enabled guilds
        await using (var uow = _db.GetDbContext())
        {
            var guilds = _client.Guilds.Select(x => x.Id).ToList();
            var enabled = await uow.GetTable<GreetSettings>()
                                   .Where(x => x.GuildId.In(guilds))
                                   .Where(x => x.IsEnabled)
                                   .Select(x => new
                                   {
                                       x.GuildId,
                                       x.GreetType
                                   })
                                   .ToListAsync();

            foreach (var e in enabled)
            {
                _enabled[e.GreetType].Add(e.GuildId);
            }
        }

        _client.UserJoined += OnUserJoined;
        _client.UserLeft += OnUserLeft;

        _client.LeftGuild += OnClientLeftGuild;

        _client.GuildMemberUpdated += ClientOnGuildMemberUpdated;

        _ = Task.Run(GreetLoopInternalAsync);
    }

    private async Task GreetLoopInternalAsync()
    {
        while (true)
        {
            try
            {
                var (guildId, item) = await _greetQueue.DequeueAsync();
                await ProcessInternalAsync(guildId, item);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Greet Loop almost crashed. Please report this!");
            }

            await Task.Delay(_sendInterval);
        }
    }

    private async Task ProcessInternalAsync(ulong guildId, GreetItem item)
    {
        var conf = await GetGreetSettingsAsync(guildId, item.Type);
        if (conf is not { IsEnabled: true })
            return;

        if (item.Type == GreetType.GreetDm)
        {
            if (item.User is IGuildUser gu)
                await GreetDmUserInternal(conf, gu);
            return;
        }

        // an unavailable guild has no channels in the cache, which must not turn the greet off
        var guild = _client.GetGuild(guildId);
        if (guild is not { IsConnected: true })
            return;

        var channel = conf.ChannelId is { } cid ? guild.GetTextChannel(cid) : null;
        if (channel is null)
        {
            Log.Warning("Channel {ChannelId} in {GuildId} was not found. {GreetType} message will be disabled",
                conf.ChannelId,
                guildId,
                item.Type);
            await SetGreet(guildId, null, item.Type, false);
            return;
        }

        await GreetUsers(conf, channel, item.User);
    }

    private void Enqueue(ulong guildId, GreetType type, IUser user)
    {
        if (!_enabled[type].Contains(guildId))
            return;

        if (!_greetQueue.TryEnqueue(guildId, new(type, user), out var firstDrop) && firstDrop)
            Log.Warning("Too many pending greet messages in {GuildId}, the bot drops new ones until the queue is empty",
                guildId);
    }

    private Task ClientOnGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> optOldUser, SocketGuildUser newUser)
    {
        // a new booster, or a member who boosted the same server again
        if ((optOldUser.Value is { PremiumSince: null } && newUser is { PremiumSince: not null })
            || (optOldUser.Value?.PremiumSince is { } oldDate
                && newUser.PremiumSince is { } newDate
                && newDate > oldDate))
        {
            Enqueue(newUser.Guild.Id, GreetType.Boost, newUser);
        }

        return Task.CompletedTask;
    }

    private async Task OnClientLeftGuild(SocketGuild guild)
    {
        foreach (var gt in Enum.GetValues<GreetType>())
        {
            _enabled[gt].TryRemove(guild.Id);
        }

        await using var uow = _db.GetDbContext();
        await uow.GetTable<GreetSettings>()
                 .Where(x => x.GuildId == guild.Id)
                 .DeleteAsync();
    }

    private Task OnUserJoined(IGuildUser user)
    {
        Enqueue(user.GuildId, GreetType.Greet, user);
        Enqueue(user.GuildId, GreetType.GreetDm, user);
        return Task.CompletedTask;
    }

    private Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        Enqueue(guild.Id, GreetType.Bye, user);
        return Task.CompletedTask;
    }

    private TypedKey<GreetSettings?> GreetSettingsKey(ulong gid, GreetType type)
        => new($"greet_settings:{gid}:{type}");

    public async Task<GreetSettings?> GetGreetSettingsAsync(ulong gid, GreetType type)
        => await _cache.GetOrAddAsync<GreetSettings?>(GreetSettingsKey(gid, type),
            () => InternalGetGreetSettingsAsync(gid, type),
            TimeSpan.FromSeconds(3));

    private async Task<GreetSettings?> InternalGetGreetSettingsAsync(ulong gid, GreetType type)
    {
        await using var uow = _db.GetDbContext();
        var res = await uow.GetTable<GreetSettings>()
                           .Where(x => x.GuildId == gid && x.GreetType == type)
                           .FirstOrDefaultAsync();

        if (res is not null)
            res.MessageText ??= GetDefaultGreet(type);

        return res;
    }

    private async Task GreetUsers(GreetSettings conf, ITextChannel channel, IUser user)
    {
        var repCtx = new ReplacementContext(client: _client,
            guild: channel.Guild,
            channel: channel,
            user: user);

        var template = SmartText.CreateFrom(conf.MessageText);
        var text = await _repSvc.ReplaceAsync(template, repCtx);
        try
        {
            var toDelete = await _sender.Response(channel)
                                        .Text(text)
                                        .Mentions(TemplateMentions.FromTemplate(template))
                                        .SendAsync();
            if (conf.AutoDeleteTimer > 0)
                toDelete.DeleteAfter(conf.AutoDeleteTimer);
        }
        catch (HttpException ex) when (ex.DiscordCode is DiscordErrorCode.InsufficientPermissions
                                           or DiscordErrorCode.MissingPermissions
                                           or DiscordErrorCode.UnknownChannel)
        {
            Log.Warning(ex,
                "Missing permissions to send a {GreetType} message, it will be disabled on server: {GuildId}",
                conf.GreetType,
                channel.GuildId);
            await SetGreet(channel.GuildId, channel.Id, conf.GreetType, false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error embeding greet message");
        }
    }

    private async Task<bool> GreetDmUserInternal(GreetSettings conf, IGuildUser user)
    {
        try
        {
            var repCtx = new ReplacementContext(client: _client, guild: user.Guild, user: user);
            var smartText = SmartText.CreateFrom(conf.MessageText);
            smartText = await _repSvc.ReplaceAsync(smartText, repCtx);

            if (smartText is SmartPlainText pt)
            {
                smartText = new SmartEmbedTextArray
                {
                    Embeds =
                    [
                        new SmartEmbedArrayElementText
                        {
                            Description = pt.Text,
                            Footer = CreateFooterSource(user),
                        },
                    ],
                };
            }
            else if (smartText is SmartEmbedTextArray seta)
            {
                // if the greet dm message is a text array
                var ebElem = seta.Embeds.LastOrDefault();
                if (ebElem is null)
                {
                    // if there are no embeds, add an embed with the footer
                    smartText = seta with
                    {
                        Embeds =
                        [
                            new SmartEmbedArrayElementText()
                            {
                                Footer = CreateFooterSource(user)
                            }
                        ]
                    };
                }
                else
                {
                    // if the maximum amount of embeds is reached, edit the last embed
                    if (seta.Embeds.Length >= 10)
                    {
                        seta.Embeds[^1] = seta.Embeds[^1] with
                        {
                            Footer = CreateFooterSource(user)
                        };
                    }
                    else
                    {
                        // if there is less than 10 embeds, add an embed with footer only
                        seta.Embeds = seta.Embeds.Append(new SmartEmbedArrayElementText()
                                          {
                                              Footer = CreateFooterSource(user)
                                          })
                                          .ToArray();
                    }
                }
            }

            await _sender.Response(user).Text(smartText).Sanitize(false).SendAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to send Greet DM. Probably the user has closed DMs");
            return false;
        }

        return true;
    }

    private static SmartTextEmbedFooter CreateFooterSource(IGuildUser user)
        => new()
        {
            Text = $"This message was sent from {user.Guild} server.",
            IconUrl = user.Guild.IconUrl
        };


    public static string GetDefaultGreet(GreetType greetType)
        => greetType switch
        {
            GreetType.Boost => "%user.mention% has boosted the server!",
            GreetType.Greet => "%user.mention% has joined the server!",
            GreetType.Bye => "%user.name% has left the server!",
            GreetType.GreetDm => "Welcome to the server %user.name%",
            _ => "%user.name% did something new!"
        };

    public async Task<bool> SetGreet(
        ulong guildId,
        ulong? channelId,
        GreetType greetType,
        bool? value = null)
    {
        await using var uow = _db.GetDbContext();
        var q = uow.GetTable<GreetSettings>();

        if (value is null)
            value = !_enabled[greetType].Contains(guildId);

        if (value is { } v)
        {
            await q
                .InsertOrUpdateAsync(() => new()
                    {
                        GuildId = guildId,
                        GreetType = greetType,
                        IsEnabled = v,
                        ChannelId = channelId,
                    },
                    (old) => new()
                    {
                        IsEnabled = v,
                        ChannelId = channelId,
                    },
                    () => new()
                    {
                        GuildId = guildId,
                        GreetType = greetType,
                    });

            await _cache.RemoveAsync(GreetSettingsKey(guildId, greetType));
        }

        if (value is true)
        {
            _enabled[greetType].Add(guildId);
            return true;
        }

        _enabled[greetType].TryRemove(guildId);
        return false;
    }


    public async Task<bool> SetMessage(ulong guildId, GreetType greetType, string? message)
    {
        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<GreetSettings>()
                     .InsertOrUpdateAsync(() => new()
                         {
                             GuildId = guildId,
                             GreetType = greetType,
                             MessageText = message
                         },
                         x => new()
                         {
                             MessageText = message
                         },
                         () => new()
                         {
                             GuildId = guildId,
                             GreetType = greetType
                         });
        }

        var conf = await GetGreetSettingsAsync(guildId, greetType);

        return conf?.IsEnabled ?? false;
    }

    public async Task<bool> SetDeleteTimer(ulong guildId, GreetType greetType, int timer)
    {
        if (timer < 0 || timer > 3600)
            throw new ArgumentOutOfRangeException(nameof(timer));

        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<GreetSettings>()
                     .InsertOrUpdateAsync(() => new()
                         {
                             GuildId = guildId,
                             GreetType = greetType,
                             AutoDeleteTimer = timer,
                         },
                         x => new()
                         {
                             AutoDeleteTimer = timer
                         },
                         () => new()
                         {
                             GuildId = guildId,
                             GreetType = greetType
                         });
        }

        var conf = await GetGreetSettingsAsync(guildId, greetType);

        return conf?.IsEnabled ?? false;
    }


    public async Task<bool> Test(
        ulong guildId,
        GreetType type,
        IMessageChannel channel,
        IGuildUser user)
    {
        var conf = await GetGreetSettingsAsync(guildId, type);
        if (conf is null)
        {
            conf = new GreetSettings()
            {
                ChannelId = channel.Id,
                GreetType = type,
                IsEnabled = false,
                GuildId = guildId,
                AutoDeleteTimer = 30,
                MessageText = GetDefaultGreet(type)
            };
        }

        await SendMessage(conf, channel, user);
        return true;
    }

    public async Task<bool> SendMessage(GreetSettings conf, IMessageChannel channel, IGuildUser user)
    {
        if (conf.GreetType == GreetType.GreetDm)
            return await GreetDmUserInternal(conf, user);

        if (channel is not ITextChannel ch)
            return false;

        await GreetUsers(conf, ch, user);
        return true;
    }
}