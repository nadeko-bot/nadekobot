using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public sealed class GameVoiceChannelService(DiscordSocketClient client, DbService db, ShardData shardData)
    : INService, IReadyExecutor
{
    private static readonly TimeSpan _moveDelay = TimeSpan.FromSeconds(1);

    private readonly ConcurrentHashSet<ulong> _gameVoiceChannels = new();

    public async Task OnReadyAsync()
    {
        await using (var uow = db.GetDbContext())
        {
            var ids = await uow.GetTable<GuildConfig>()
                               .Where(Queries.GuildOnShard<GuildConfig>(x => x.GuildId,
                                   shardData.TotalShards,
                                   shardData.ShardId))
                               .Where(gc => gc.GameVoiceChannel != null)
                               .Select(gc => gc.GameVoiceChannel!.Value)
                               .ToListAsyncLinqToDB();

            foreach (var id in ids)
                _gameVoiceChannels.Add(id);
        }

        client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
        client.PresenceUpdated += OnPresenceUpdate;
    }

    public ulong? ToggleGameVoiceChannel(ulong guildId, ulong vchId)
    {
        ulong? id;
        using var uow = db.GetDbContext();
        var gc = uow.GuildConfigsForId(guildId);

        if (gc.GameVoiceChannel == vchId)
        {
            _gameVoiceChannels.TryRemove(vchId);
            id = gc.GameVoiceChannel = null;
        }
        else
        {
            if (gc.GameVoiceChannel is { } oldId)
                _gameVoiceChannels.TryRemove(oldId);
            _gameVoiceChannels.Add(vchId);
            id = gc.GameVoiceChannel = vchId;
        }

        uow.SaveChanges();
        return id;
    }

    private Task OnPresenceUpdate(SocketUser socketUser, SocketPresence before, SocketPresence after)
    {
        if (socketUser is not SocketGuildUser { VoiceChannel: { } vc } user
            || !_gameVoiceChannels.Contains(vc.Id))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var activity in after.Activities)
                {
                    if (activity.Type == ActivityType.Playing && await TriggerGvcInternalAsync(user, activity.Name))
                        return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error running PresenceUpdated in gvc");
            }
        });

        return Task.CompletedTask;
    }

    private Task OnUserVoiceStateUpdated(SocketUser usr, SocketVoiceState oldState, SocketVoiceState newState)
    {
        if (usr is not SocketGuildUser user
            || newState.VoiceChannel is not { } vc
            || !_gameVoiceChannels.Contains(vc.Id))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var activity in user.Activities)
                {
                    if (await TriggerGvcInternalAsync(user, activity.Name))
                        return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error running VoiceStateUpdate in gvc");
            }
        });

        return Task.CompletedTask;
    }

    private async Task<bool> TriggerGvcInternalAsync(SocketGuildUser user, string? game)
    {
        if (string.IsNullOrWhiteSpace(game))
            return false;

        var name = game.AsSpan().Trim();
        SocketVoiceChannel? target = null;
        foreach (var ch in user.Guild.VoiceChannels)
        {
            if (name.Equals(ch.Name, StringComparison.InvariantCultureIgnoreCase))
            {
                target = ch;
                break;
            }
        }

        if (target is null)
            return false;

        if (user.VoiceChannel?.Id == target.Id)
            return true;

        await Task.Delay(_moveDelay);
        await user.ModifyAsync(gu => gu.Channel = target);
        return true;
    }
}
