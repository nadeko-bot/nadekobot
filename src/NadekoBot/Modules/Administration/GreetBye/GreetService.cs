using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using LinqToDB.Tools;
using NadekoBot.Common.ModuleBehaviors;
using System.Threading.Channels;

namespace NadekoBot.Services;

public class GreetService : INService, IReadyExecutor
{
    private readonly DbService _db;

    private ConcurrentDictionary<GreetType, ConcurrentHashSet<ulong>> _enabled = new();

    private readonly DiscordSocketClient _client;

    private readonly IReplacementService _repSvc;
    private readonly IBotCache _cache;
    private readonly IMessageSenderService _sender;

    private readonly Channel<(GreetSettings, IUser, ITextChannel?)> _greetQueue =
        Channel.CreateBounded<(GreetSettings, IUser, ITextChannel?)>(
            new BoundedChannelOptions(60)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

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

        while (true)
        {
            try
            {
                var (conf, user, ch) = await _greetQueue.Reader.ReadAsync();
                await GreetUsers(conf, ch, user);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Greet Loop almost crashed. Please report this!");
            }

            await Task.Delay(2016);
        }
    }

    private Task ClientOnGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> optOldUser, SocketGuildUser newUser)
    {
        if (!_enabled[GreetType.Boost].Contains(newUser.Guild.Id))
            return Task.CompletedTask;

        // if user is a new booster
        // or boosted again the same server
        if ((optOldUser.Value is { PremiumSince: null } && newUser is { PremiumSince: not null })
            || (optOldUser.Value?.PremiumSince is { } oldDate
                && newUser.PremiumSince is { } newDate
                && newDate > oldDate))
        {
            _ = Task.Run(async () =>
            {
                var conf = await GetGreetSettingsAsync(newUser.Guild.Id, GreetType.Boost);

                if (conf is null || !conf.IsEnabled)
                    return;

                ITextChannel? channel = null;
                if (conf.ChannelId is { } cid)
                    channel = newUser.Guild.GetTextChannel(cid);

                if (channel is null)
                    return;

                await GreetUsers(conf, channel, newUser);
            });
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
        _ = Task.Run(async () =>
        {
            try
            {
                if (_enabled[GreetType.Greet].Contains(user.GuildId))
                {
                    var conf = await GetGreetSettingsAsync(user.GuildId, GreetType.Greet);
                    if (conf?.ChannelId is ulong cid)
                    {
                        var channel = await user.Guild.GetTextChannelAsync(cid);
                        if (channel is not null)
                        {
                            await _greetQueue.Writer.WriteAsync((conf, user, channel));
                        }
                    }
                }


                if (_enabled[GreetType.GreetDm].Contains(user.GuildId))
                {
                    var confDm = await GetGreetSettingsAsync(user.GuildId, GreetType.GreetDm);
                    if (confDm is not null)
                    {
                        await _greetQueue.Writer.WriteAsync((confDm, user, null));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in GreetService.OnUserJoined. This should not happen. Please report it");
            }
        });
        return Task.CompletedTask;
    }

    private Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        _ = Task.Run(async () =>
        {
            if (!_enabled[GreetType.Bye].Contains(guild.Id))
                return;

            try
            {
                var conf = await GetGreetSettingsAsync(guild.Id, GreetType.Bye);

                if (conf?.ChannelId is not { } cid)
                    return;

                var channel = guild.GetChannel(cid) as ITextChannel;
                if (channel is null) //maybe warn the server owner that the channel is missing
                {
                    Log.Warning("Channel {ChannelId} in {GuildId} was not found. Bye message will be disabled",
                        conf.ChannelId,
                        conf.GuildId);
                    await SetGreet(guild.Id, null, GreetType.Bye, false);
                    return;
                }

                await _greetQueue.Writer.WriteAsync((conf, user, channel));
            }
            catch
            {
                // ignored
            }
        });
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

    private async Task GreetUsers(GreetSettings conf, ITextChannel? channel, IUser user)
    {
        if (conf.GreetType == GreetType.GreetDm)
        {
            if (user is not IGuildUser gu)
                return;

            await GreetDmUserInternal(conf, gu);
            return;
        }

        if (channel is null)
            return;

        var repCtx = new ReplacementContext(client: _client,
            guild: channel.Guild,
            channel: channel,
            user: user);

        var text = SmartText.CreateFrom(conf.MessageText);
        text = await _repSvc.ReplaceAsync(text, repCtx);
        try
        {
            var toDelete = await _sender.Response(channel).Text(text).Sanitize(false).SendAsync();
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
                smartText = new SmartEmbedText()
                {
                    Description = pt.Text
                };
            }

            if (smartText is SmartEmbedText set)
            {
                smartText = set with
                {
                    Footer = CreateFooterSource(user)
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
        {
            await _greetQueue.Writer.WriteAsync((conf, user, null));
            return true;
        }

        if (channel is not ITextChannel ch)
            return false;

        await GreetUsers(conf, ch, user);
        return true;
    }
}