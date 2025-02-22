#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public sealed class BotActivityService : IBotActivityService, IReadyExecutor, INService
{
    private readonly TypedKey<ActivityPubData> _activitySetKey = new("activity.set");

    private readonly IPubSub _pubSub;
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly IReplacementService _rep;
    private readonly BotConfigService _bss;

    public BotActivityService(
        IPubSub pubSub,
        DiscordSocketClient client,
        DbService db,
        IReplacementService rep,
        BotConfigService bss)
    {
        _pubSub = pubSub;
        _client = client;
        _db = db;
        _rep = rep;
        _bss = bss;
    }

    public async Task<string> RemovePlayingAsync(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        await using var uow = _db.GetDbContext();
        var toRemove = await uow.Set<RotatingPlayingStatus>()
                                .AsQueryable()
                                .AsNoTracking()
                                .Skip(index)
                                .FirstOrDefaultAsync();

        if (toRemove is null)
            return null;

        uow.Remove(toRemove);
        await uow.SaveChangesAsync();
        return toRemove.Status;
    }

    public async Task AddPlaying(ActivityType activityType, string status)
    {
        await using var uow = _db.GetDbContext();
        var toAdd = new RotatingPlayingStatus
        {
            Status = status,
            Type = (NadekoBot.Db.DbActivityType)activityType
        };
        uow.Add(toAdd);
        await uow.SaveChangesAsync();
    }

    public void DisableRotatePlaying()
    {
        _bss.ModifyConfig(bs => { bs.RotateStatuses = false; });
    }

    public bool ToggleRotatePlaying()
    {
        var enabled = false;
        _bss.ModifyConfig(bs => { enabled = bs.RotateStatuses = !bs.RotateStatuses; });
        return enabled;
    }

    public IReadOnlyList<RotatingPlayingStatus> GetRotatingStatuses()
    {
        using var uow = _db.GetDbContext();
        return uow.Set<RotatingPlayingStatus>().AsNoTracking().ToList();
    }

    public Task SetActivityAsync(string game, ActivityType? type)
        => _pubSub.Pub(_activitySetKey,
            new()
            {
                Name = game,
                Link = null,
                Type = type
            });

    public Task SetStreamAsync(string name, string link)
        => _pubSub.Pub(_activitySetKey,
            new()
            {
                Name = name,
                Link = link,
                Type = ActivityType.Streaming
            });

    private sealed class ActivityPubData
    {
        public string Name { get; init; }
        public string Link { get; init; }
        public ActivityType? Type { get; init; }
    }

    public async Task OnReadyAsync()
    {
        await _pubSub.Sub(_activitySetKey,
            async data =>
            {
                if (_client.ShardId == 0)
                {
                    DisableRotatePlaying();
                }

                try
                {
                    if (data.Type is { } activityType)
                    {
                        await _client.SetGameAsync(data.Name, data.Link, activityType);
                    }
                    else
                    {
                        await _client.SetCustomStatusAsync(data.Name);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error setting activity");
                }
            });

        if (_client.ShardId != 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var index = 0;
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                if (!_bss.Data.RotateStatuses)
                    continue;

                IReadOnlyList<RotatingPlayingStatus> rotatingStatuses;
                await using (var uow = _db.GetDbContext())
                {
                    rotatingStatuses = uow.Set<RotatingPlayingStatus>().AsNoTracking().OrderBy(x => x.Id).ToList();
                }

                if (rotatingStatuses.Count == 0)
                    continue;

                var playingStatus = index >= rotatingStatuses.Count
                    ? rotatingStatuses[index = 0]
                    : rotatingStatuses[index++];

                var statusText = await _rep.ReplaceAsync(playingStatus.Status, new(client: _client));
                await SetActivityAsync(statusText, (ActivityType)playingStatus.Type);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Rotating playing status errored: {ErrorMessage}", ex.Message);
            }
        }
    }
}