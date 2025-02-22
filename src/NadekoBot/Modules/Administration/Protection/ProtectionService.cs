#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Threading.Channels;

namespace NadekoBot.Modules.Administration.Services;

public class ProtectionService : IReadyExecutor, INService
{
    public event Func<PunishmentAction, ProtectionType, IGuildUser[], Task> OnAntiProtectionTriggered = delegate
    {
        return Task.CompletedTask;
    };

    private readonly ConcurrentDictionary<ulong, AntiRaidStats> _antiRaidGuilds = new();

    private readonly ConcurrentDictionary<ulong, AntiSpamStats> _antiSpamGuilds = new();

    private readonly ConcurrentDictionary<ulong, AntiAltStats> _antiAltGuilds = new();

    private readonly DiscordSocketClient _client;
    private readonly MuteService _mute;
    private readonly DbService _db;
    private readonly UserPunishService _punishService;
    private readonly INotifySubscriber _notifySub;
    private readonly ShardData _shardData;
    private readonly Channel<PunishQueueItem> _punishUserQueue =
        Channel.CreateUnbounded<PunishQueueItem>(new()
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ProtectionService(
        DiscordSocketClient client,
        MuteService mute,
        DbService db,
        UserPunishService punishService,
        INotifySubscriber notifySub,
        ShardData shardData)
    {
        _client = client;
        _mute = mute;
        _db = db;
        _punishService = punishService;
        _notifySub = notifySub;
        _shardData = shardData;

        _client.MessageReceived += HandleAntiSpam;
        _client.UserJoined += HandleUserJoined;

        _client.LeftGuild += _client_LeftGuild;
    }

    private async Task RunQueue()
    {
        while (true)
        {
            var item = await _punishUserQueue.Reader.ReadAsync();

            var muteTime = item.MuteTime;
            var gu = item.User;
            try
            {
                await _punishService.ApplyPunishment(gu.Guild,
                    gu,
                    _client.CurrentUser,
                    item.Action,
                    muteTime,
                    item.RoleId,
                    $"{item.Type} Protection");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in punish queue: {Message}", ex.Message);
            }
            finally
            {
                await Task.Delay(1000);
            }
        }
    }

    private Task _client_LeftGuild(SocketGuild guild)
    {
        _ = Task.Run(async () =>
        {
            await TryStopAntiRaidAsync(guild.Id);
            await TryStopAntiSpamAsync(guild.Id);
            await TryStopAntiAltAsync(guild.Id);
        });
        return Task.CompletedTask;
    }

    private Task HandleUserJoined(SocketGuildUser user)
    {
        if (user.IsBot)
            return Task.CompletedTask;

        _antiRaidGuilds.TryGetValue(user.Guild.Id, out var maybeStats);
        _antiAltGuilds.TryGetValue(user.Guild.Id, out var maybeAlts);

        if (maybeStats is null && maybeAlts is null)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            if (maybeAlts is { } alts)
            {
                if (user.CreatedAt != default)
                {
                    var diff = DateTime.UtcNow - user.CreatedAt.UtcDateTime;
                    if (diff < alts.MinAge)
                    {
                        alts.Increment();

                        await PunishUsers(alts.Action,
                            ProtectionType.Alting,
                            alts.ActionDurationMinutes,
                            alts.RoleId,
                            user);

                        await _notifySub.NotifyAsync(new ProtectionNotifyModel(user.Guild.Id,
                            ProtectionType.Alting,
                            user.Id));
                        return;
                    }
                }
            }

            try
            {
                if (maybeStats is not { } stats || !stats.RaidUsers.Add(user))
                    return;

                ++stats.UsersCount;

                if (stats.UsersCount >= stats.AntiRaidSettings.UserThreshold)
                {
                    var users = stats.RaidUsers.ToArray();
                    stats.RaidUsers.Clear();
                    var settings = stats.AntiRaidSettings;

                    await PunishUsers(settings.Action, ProtectionType.Raiding, settings.PunishDuration, null, users);
                    await _notifySub.NotifyAsync(
                        new ProtectionNotifyModel(user.Guild.Id, ProtectionType.Raiding, users[0].Id)
                    );
                }

                await Task.Delay(1000 * stats.AntiRaidSettings.Seconds);

                stats.RaidUsers.TryRemove(user);
                --stats.UsersCount;
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task HandleAntiSpam(SocketMessage arg)
    {
        if (arg is not SocketUserMessage msg || msg.Author.IsBot)
            return Task.CompletedTask;

        if (msg.Channel is not ITextChannel channel)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_antiSpamGuilds.TryGetValue(channel.Guild.Id, out var spamSettings)
                    || spamSettings.AntiSpamSettings.IgnoredChannels.Contains(new()
                    {
                        ChannelId = channel.Id
                    }))
                    return;

                var stats = spamSettings.UserStats.AddOrUpdate(msg.Author.Id,
                    _ => new(msg),
                    (_, old) =>
                    {
                        old.ApplyNextMessage(msg);
                        return old;
                    });

                if (stats.Count >= spamSettings.AntiSpamSettings.MessageThreshold)
                {
                    if (spamSettings.UserStats.TryRemove(msg.Author.Id, out stats))
                    {
                        var settings = spamSettings.AntiSpamSettings;
                        await PunishUsers(settings.Action,
                            ProtectionType.Spamming,
                            settings.MuteTime,
                            settings.RoleId,
                            (IGuildUser)msg.Author);

                        await _notifySub.NotifyAsync(new ProtectionNotifyModel(channel.GuildId,
                            ProtectionType.Spamming,
                            msg.Author.Id));
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

    private async Task PunishUsers(
        PunishmentAction action,
        ProtectionType pt,
        int muteTime,
        ulong? roleId,
        params IGuildUser[] gus)
    {
        Log.Information("[{PunishType}] - Punishing [{Count}] users with [{PunishAction}] in {GuildName} guild",
            pt,
            gus.Length,
            action,
            gus[0].Guild.Name);

        foreach (var gu in gus)
        {
            await _punishUserQueue.Writer.WriteAsync(new()
            {
                Action = action,
                Type = pt,
                User = gu,
                MuteTime = muteTime,
                RoleId = roleId
            });
        }

        _ = OnAntiProtectionTriggered(action, pt, gus);
    }

    public async Task<AntiRaidStats> StartAntiRaidAsync(
        ulong guildId,
        int userThreshold,
        int seconds,
        PunishmentAction action,
        int minutesDuration)
    {
        var g = _client.GetGuild(guildId);
        await _mute.GetMuteRole(g);

        if (action == PunishmentAction.AddRole)
            return null;

        if (!IsDurationAllowed(action))
            minutesDuration = 0;

        var stats = new AntiRaidStats
        {
            AntiRaidSettings = new()
            {
                Action = action,
                Seconds = seconds,
                UserThreshold = userThreshold,
                PunishDuration = minutesDuration
            }
        };

        _antiRaidGuilds.AddOrUpdate(guildId, stats, (_, _) => stats);

        await using var uow = _db.GetDbContext();

        await uow.GetTable<AntiRaidSetting>()
           .InsertOrUpdateAsync(() => new()
           {
               GuildId = guildId,
               Action = action,
               Seconds = seconds,
               UserThreshold = userThreshold,
               PunishDuration = minutesDuration
           }, _ => new()
           {
               Action = action,
               Seconds = seconds,
               UserThreshold = userThreshold,
               PunishDuration = minutesDuration
           }, () => new()
           {
               GuildId = guildId
           });


        return stats;
    }

    public async Task<bool> TryStopAntiRaidAsync(ulong guildId)
    {
        if (_antiRaidGuilds.TryRemove(guildId, out _))
        {
            using var uow = _db.GetDbContext();
            await uow.GetTable<AntiRaidSetting>()
                .Where(x => x.GuildId == guildId)
                .DeleteAsync();

            return true;
        }

        return false;
    }

    public async Task<bool> TryStopAntiSpamAsync(ulong guildId)
    {
        if (_antiSpamGuilds.TryRemove(guildId, out _))
        {
            using var uow = _db.GetDbContext();
            await uow.GetTable<AntiSpamSetting>()
                .Where(x => x.GuildId == guildId)
                .DeleteAsync();

            return true;
        }

        return false;
    }

    public async Task<AntiSpamStats> StartAntiSpamAsync(
        ulong guildId,
        int messageCount,
        PunishmentAction action,
        int punishDurationMinutes,
        ulong? roleId)
    {
        var g = _client.GetGuild(guildId);
        await _mute.GetMuteRole(g);

        if (!IsDurationAllowed(action))
            punishDurationMinutes = 0;

        var stats = new AntiSpamStats
        {
            AntiSpamSettings = new()
            {
                Action = action,
                MessageThreshold = messageCount,
                MuteTime = punishDurationMinutes,
                RoleId = roleId
            }
        };

        _antiSpamGuilds.AddOrUpdate(guildId,
            stats,
            (_, old) =>
            {
                stats.AntiSpamSettings.IgnoredChannels = old.AntiSpamSettings.IgnoredChannels;
                return stats;
            });

        await using var uow = _db.GetDbContext();
        await uow.GetTable<AntiSpamSetting>()
            .InsertOrUpdateAsync(() => new()
            {
                GuildId = guildId,
                Action = stats.AntiSpamSettings.Action,
                MessageThreshold = stats.AntiSpamSettings.MessageThreshold,
                MuteTime = stats.AntiSpamSettings.MuteTime,
                RoleId = stats.AntiSpamSettings.RoleId
            }, (old) => new()
            {
                GuildId = guildId,
                Action = stats.AntiSpamSettings.Action,
                MessageThreshold = stats.AntiSpamSettings.MessageThreshold,
                MuteTime = stats.AntiSpamSettings.MuteTime,
                RoleId = stats.AntiSpamSettings.RoleId
            }, () => new()
            {
                GuildId = guildId
            });

        return stats;
    }

    public async Task<bool?> AntiSpamIgnoreAsync(ulong guildId, ulong channelId)
    {
        var obj = new AntiSpamIgnore
        {
            ChannelId = channelId
        };
        
        await using var uow = _db.GetDbContext();
        var spam = await uow.GetTable<AntiSpamSetting>()
            .Include(x => x.IgnoredChannels)
            .Where(x => x.GuildId == guildId)
            .FirstOrDefaultAsyncEF();
        
        if (spam is null)
            return null;

        var added = false;
        if (spam.IgnoredChannels.All(x => x.ChannelId != channelId))
        {
            if (_antiSpamGuilds.TryGetValue(guildId, out var temp))
                temp.AntiSpamSettings.IgnoredChannels.Add(obj); // add to local cache

            added = true;
        }
        else
        {
            var toRemove = spam.IgnoredChannels.First(x => x.ChannelId == channelId);

            uow.Set<AntiSpamIgnore>().Remove(toRemove);

            if (_antiSpamGuilds.TryGetValue(guildId, out var temp))
                temp.AntiSpamSettings.IgnoredChannels.Remove(toRemove); // remove from local cache

            added = false;
        }

        await uow.SaveChangesAsync();
        return added;
    }

    public (AntiSpamStats, AntiRaidStats, AntiAltStats) GetAntiStats(ulong guildId)
    {
        _antiRaidGuilds.TryGetValue(guildId, out var antiRaidStats);
        _antiSpamGuilds.TryGetValue(guildId, out var antiSpamStats);
        _antiAltGuilds.TryGetValue(guildId, out var antiAltStats);

        return (antiSpamStats, antiRaidStats, antiAltStats);
    }

    public bool IsDurationAllowed(PunishmentAction action)
    {
        switch (action)
        {
            case PunishmentAction.Ban:
            case PunishmentAction.Mute:
            case PunishmentAction.ChatMute:
            case PunishmentAction.VoiceMute:
            case PunishmentAction.AddRole:
            case PunishmentAction.TimeOut:
                return true;
            default:
                return false;
        }
    }

    public async Task StartAntiAltAsync(
        ulong guildId,
        int minAgeMinutes,
        PunishmentAction action,
        int actionDurationMinutes = 0,
        ulong? roleId = null)
    {
        await using var uow = _db.GetDbContext();

        await uow.GetTable<AntiAltSetting>()
            .InsertOrUpdateAsync(() => new()
            {
                GuildId = guildId,
                Action = action,
                ActionDurationMinutes = actionDurationMinutes,
                MinAge = TimeSpan.FromMinutes(minAgeMinutes),
                RoleId = roleId
            }, _ => new()
            {
                Action = action,
                ActionDurationMinutes = actionDurationMinutes,
                MinAge = TimeSpan.FromMinutes(minAgeMinutes),
                RoleId = roleId
            }, () => new()
            {
                GuildId = guildId
            });

        _antiAltGuilds[guildId] = new(new()
        {
            GuildId = guildId,
            Action = action,
            ActionDurationMinutes = actionDurationMinutes,
            MinAge = TimeSpan.FromMinutes(minAgeMinutes),
            RoleId = roleId
        });
    }

    public async Task<bool> TryStopAntiAltAsync(ulong guildId)
    {
        if (!_antiAltGuilds.TryRemove(guildId, out _))
            return false;

        await using var uow = _db.GetDbContext();
        await uow.GetTable<AntiAltSetting>()
            .Where(x => x.GuildId == guildId)
            .DeleteAsync();
        return true;
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        var configs = await uow.GetTable<AntiAltSetting>()
            .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
            .ToListAsyncLinqToDB();

        foreach (var config in configs)
            _antiAltGuilds[config.GuildId] = new(config);

        var raidConfigs = await uow.GetTable<AntiRaidSetting>()
            .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
            .ToListAsyncLinqToDB();

        foreach (var config in raidConfigs)
        {
            _antiRaidGuilds[config.GuildId] = new()
            {
                AntiRaidSettings = config,
            };
        }

        var spamConfigs = await uow.GetTable<AntiSpamSetting>()
            .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
            .ToListAsyncLinqToDB();

        foreach (var config in spamConfigs)
        {
            _antiSpamGuilds[config.GuildId] = new()
            {
                AntiSpamSettings = config,
            };
        }

        await RunQueue();
    }
}