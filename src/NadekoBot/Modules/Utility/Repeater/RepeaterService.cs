using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.Services;

public sealed class RepeaterService : IReadyExecutor, INService
{
    private const int MAX_REPEATERS = 5;

    private readonly DbService _db;
    private readonly IReplacementService _repSvc;
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;
    private readonly LinkedList<RunningRepeater> _repeaterQueue;
    private readonly ConcurrentHashSet<int> _noRedundant;
    private readonly ConcurrentHashSet<int> _skipNext = new();

    private readonly object _queueLocker = new();
    private readonly IMessageSenderService _sender;

    public RepeaterService(
        DiscordSocketClient client,
        DbService db,
        IReplacementService repSvc,
        IBotCreds creds,
        IMessageSenderService sender)
    {
        _db = db;
        _repSvc = repSvc;
        _creds = creds;
        _client = client;
        _sender = sender;

        using var uow = _db.GetDbContext();
        var shardRepeaters = uow.Set<Repeater>()
                                .Where(x => (int)(x.GuildId / Math.Pow(2, 22)) % _creds.TotalShards
                                            == _client.ShardId)
                                .AsNoTracking()
                                .ToList();

        _noRedundant = new(shardRepeaters.Where(x => x.NoRedundant).Select(x => x.Id));

        _repeaterQueue = new(shardRepeaters.Select(rep => new RunningRepeater(rep)).OrderBy(x => x.NextTime));
    }

    public Task OnReadyAsync()
    {
        _ = Task.Run(RunRepeatersLoop);
        return Task.CompletedTask;
    }

    private async Task RunRepeatersLoop()
    {
        while (true)
        {
            try
            {
                // calculate timeout for the first item
                var timeout = GetNextTimeout();

                // wait it out, and recalculate afterwards
                // because repeaters might've been modified meanwhile
                if (timeout > TimeSpan.Zero)
                {
                    await Task.Delay(timeout > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : timeout);
                    continue;
                }

                // collect (remove) all repeaters which need to run (3 seconds tolerance)
                var now = DateTime.UtcNow + TimeSpan.FromSeconds(3);

                var toExecute = new List<RunningRepeater>();
                lock (_repeaterQueue)
                {
                    var current = _repeaterQueue.First;
                    while (true)
                    {
                        if (current is null || current.Value.NextTime > now)
                            break;

                        toExecute.Add(current.Value);
                        current = current.Next;
                    }
                }

                // execute
                foreach (var chunk in toExecute.Chunk(5))
                    await chunk.Where(x => !_skipNext.TryRemove(x.Repeater.Id)).Select(Trigger).WhenAll();

                // reinsert
                foreach (var rep in toExecute)
                    await HandlePostExecute(rep);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Critical error in repeater queue: {ErrorMessage}", ex.Message);
                await Task.Delay(5000);
            }
        }
    }

    private async Task HandlePostExecute(RunningRepeater rep)
    {
        if (rep.ErrorCount >= 10)
        {
            RemoveFromQueue(rep.Repeater.Id);
            await RemoveRepeaterInternal(rep.Repeater);
            return;
        }

        UpdatePosition(rep);
    }

    private void UpdatePosition(RunningRepeater rep)
    {
        lock (_queueLocker)
        {
            rep.UpdateNextTime();
            _repeaterQueue.Remove(rep);
            AddToQueue(rep);
        }
    }

    public async Task<bool> TriggerExternal(ulong guildId, int index)
    {
        await using var uow = _db.GetDbContext();

        var toTrigger = await uow.Set<Repeater>()
                                 .AsNoTracking()
                                 .Where(x => x.GuildId == guildId)
                                 .Skip(index)
                                 .FirstOrDefaultAsyncEF();

        if (toTrigger is null)
            return false;

        LinkedListNode<RunningRepeater>? node;
        lock (_queueLocker)
        {
            node = _repeaterQueue.FindNode(x => x.Repeater.Id == toTrigger.Id);
            if (node is null)
                return false;

            _repeaterQueue.Remove(node);
        }

        await Trigger(node.Value);
        await HandlePostExecute(node.Value);
        return true;
    }

    private void AddToQueue(RunningRepeater rep)
    {
        lock (_queueLocker)
        {
            var current = _repeaterQueue.First;
            if (current is null)
            {
                _repeaterQueue.AddFirst(rep);
                return;
            }

            while (current is not null && current.Value.NextTime < rep.NextTime)
                current = current.Next;

            if (current is null)
                _repeaterQueue.AddLast(rep);
            else
                _repeaterQueue.AddBefore(current, rep);
        }
    }

    private TimeSpan GetNextTimeout()
    {
        lock (_queueLocker)
        {
            var first = _repeaterQueue.First;

            // if there are no items in the queue, just wait out the minimum duration (1 minute) and try again
            if (first is null)
                return TimeSpan.FromMinutes(1);

            return first.Value.NextTime - DateTime.UtcNow;
        }
    }

    private async Task Trigger(RunningRepeater rr)
    {
        var repeater = rr.Repeater;

        void ChannelMissingError()
        {
            rr.ErrorCount = int.MaxValue;
            Log.Warning("[Repeater] Channel [{Channelid}] for not found or insufficient permissions. "
                        + "Repeater will be removed. ",
                repeater.ChannelId);
        }

        var channel = _client.GetChannel(repeater.ChannelId) as ITextChannel;
        if (channel is null)
        {
            try
            {
                channel = await _client.Rest.GetChannelAsync(repeater.ChannelId) as ITextChannel;
            }
            catch
            {
            }
        }

        if (channel is null)
        {
            ChannelMissingError();
            return;
        }

        var guild = _client.GetGuild(channel.GuildId);
        if (guild is null)
        {
            ChannelMissingError();
            return;
        }

        if (_noRedundant.Contains(repeater.Id))
        {
            try
            {
                var lastMsgInChannel = await channel.GetMessagesAsync(2).Flatten().FirstAsync();
                if (lastMsgInChannel is not null && lastMsgInChannel.Id == repeater.LastMessageId)
                    return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "[Repeater] Error while getting last channel message in {GuildId}/{ChannelId} "
                    + "Bot probably doesn't have the permission to read message history",
                    guild.Id,
                    channel.Id);
            }
        }

        if (repeater.LastMessageId is { } lastMessageId)
        {
            try
            {
                var oldMsg = await channel.GetMessageAsync(lastMessageId);
                if (oldMsg is not null)
                    await oldMsg.DeleteAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "[Repeater] Error while deleting previous message in {GuildId}/{ChannelId}",
                    guild.Id,
                    channel.Id);
            }
        }

        var repCtx = new ReplacementContext(client: _client,
            guild: guild,
            channel: channel,
            user: guild.CurrentUser);

        try
        {
            var text = SmartText.CreateFrom(repeater.Message);
            text = await _repSvc.ReplaceAsync(text, repCtx);

            var newMsg = await _sender.Response(channel)
                                      .Text(text)
                                      .Sanitize(false)
                                      .SendAsync();
            
            _ = newMsg.AddReactionAsync(new Emoji("🔄"));

            if (_noRedundant.Contains(repeater.Id))
            {
                await SetRepeaterLastMessageInternal(repeater.Id, newMsg.Id);
                repeater.LastMessageId = newMsg.Id;
            }

            rr.ErrorCount = 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Repeater] Error sending repeat message ({ErrorCount})", rr.ErrorCount++);
        }
    }

    private async Task RemoveRepeaterInternal(Repeater r)
    {
        _noRedundant.TryRemove(r.Id);

        await using var uow = _db.GetDbContext();
        await uow.Set<Repeater>().DeleteAsync(x => x.Id == r.Id);

        await uow.SaveChangesAsync();
    }

    private RunningRepeater? RemoveFromQueue(int id)
    {
        lock (_queueLocker)
        {
            var node = _repeaterQueue.FindNode(x => x.Repeater.Id == id);
            if (node is null)
                return null;

            _repeaterQueue.Remove(node);
            return node.Value;
        }
    }

    private async Task SetRepeaterLastMessageInternal(int repeaterId, ulong lastMsgId)
    {
        await using var uow = _db.GetDbContext();
        await uow.Set<Repeater>()
                 .AsQueryable()
                 .Where(x => x.Id == repeaterId)
                 .UpdateAsync(rep => new()
                 {
                     LastMessageId = lastMsgId
                 });
    }

    public async Task<RunningRepeater?> AddRepeaterAsync(
        ulong channelId,
        ulong guildId,
        TimeSpan interval,
        string message,
        bool isNoRedundant,
        TimeSpan? startTimeOfDay)
    {
        var rep = new Repeater
        {
            ChannelId = channelId,
            GuildId = guildId,
            Interval = interval,
            Message = message,
            NoRedundant = isNoRedundant,
            LastMessageId = null,
            StartTimeOfDay = startTimeOfDay,
            DateAdded = DateTime.UtcNow
        };

        await using var uow = _db.GetDbContext();

        if (await uow.Set<Repeater>().CountAsyncEF(x => x.GuildId == guildId) < MAX_REPEATERS)
            uow.Set<Repeater>().Add(rep);
        else
            return null;

        await uow.SaveChangesAsync();

        if (isNoRedundant)
            _noRedundant.Add(rep.Id);
        var runner = new RunningRepeater(rep);
        AddToQueue(runner);
        return runner;
    }

    public async Task<RunningRepeater?> RemoveByIndexAsync(ulong guildId, int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, MAX_REPEATERS * 2);

        await using var uow = _db.GetDbContext();
        var toRemove = await uow.Set<Repeater>()
                                .AsNoTracking()
                                .Where(x => x.GuildId == guildId)
                                .Skip(index)
                                .FirstOrDefaultAsyncEF();

        if (toRemove is null)
            return null;

        // first try removing from queue because it can fail
        // while triggering. Instruct user to try again
        var removed = RemoveFromQueue(toRemove.Id);
        if (removed is null)
            return null;

        _noRedundant.TryRemove(toRemove.Id);
        uow.Set<Repeater>().Remove(toRemove);
        await uow.SaveChangesAsync();
        return removed;
    }

    public IReadOnlyCollection<RunningRepeater> GetRepeaters(ulong guildId)
    {
        lock (_queueLocker)
        {
            return _repeaterQueue.Where(x => x.Repeater.GuildId == guildId).ToList();
        }
    }

    public async Task<bool?> ToggleRedundantAsync(ulong guildId, int index)
    {
        await using var uow = _db.GetDbContext();
        var toToggle = await uow.Set<Repeater>()
                                .AsQueryable()
                                .Where(x => x.GuildId == guildId)
                                .Skip(index)
                                .FirstOrDefaultAsyncEF();

        if (toToggle is null)
            return null;

        var newValue = toToggle.NoRedundant = !toToggle.NoRedundant;
        if (newValue)
            _noRedundant.Add(toToggle.Id);
        else
            _noRedundant.TryRemove(toToggle.Id);

        await uow.SaveChangesAsync();
        return newValue;
    }

    public async Task<bool?> ToggleSkipNextAsync(ulong guildId, int index)
    {
        await using var ctx = _db.GetDbContext();
        var toSkip = await ctx.Set<Repeater>()
                              .Where(x => x.GuildId == guildId)
                              .Skip(index)
                              .FirstOrDefaultAsyncEF();

        if (toSkip is null)
            return null;

        if (_skipNext.Add(toSkip.Id))
            return true;

        _skipNext.TryRemove(toSkip.Id);
        return false;
    }

    public bool IsNoRedundant(int repeaterId)
        => _noRedundant.Contains(repeaterId);

    public bool IsRepeaterSkipped(int repeaterId)
        => _skipNext.Contains(repeaterId);
}