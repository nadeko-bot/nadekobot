#nullable disable
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Administration.Services;

public class PruneService : INService
{
    //channelids where prunes are currently occuring
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _pruningGuilds = new();
    private readonly TimeSpan _twoWeeks = TimeSpan.FromDays(14);
    private readonly ILogCommandService _logService;
    private readonly IPatronageService _ps;

    public PruneService(ILogCommandService logService, IPatronageService ps)
    {
        _logService = logService;
        _ps = ps;
    }

    public async Task<PruneResult> PruneWhere(
        IMessageChannel channel,
        int amount,
        Func<IMessage, bool> predicate,
        IProgress<(int deleted, int total)> progress,
        ulong? after = null
    )
    {
        ArgumentNullException.ThrowIfNull(channel, nameof(channel));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var originalAmount = amount;

        var gid = (channel as ITextChannel)?.GuildId ?? channel.Id;
        using var cancelSource = new CancellationTokenSource();
        if (!_pruningGuilds.TryAdd(gid, cancelSource))
            return PruneResult.AlreadyRunning;

        try
        {
            if (channel is ITextChannel tc && !await _ps.LimitHitAsync(LimitedFeatureName.Prune, tc.Guild.OwnerId))
            {
                return PruneResult.FeatureLimit;
            }

            var now = DateTime.UtcNow;
            IMessage[] msgs;
            IMessage lastMessage = null;

            while (amount > 0 && !cancelSource.IsCancellationRequested)
            {
                var dled = lastMessage is null
                    ? await channel.GetMessagesAsync(50).FlattenAsync()
                    : await channel.GetMessagesAsync(lastMessage, Direction.Before, 50).FlattenAsync();

                msgs = dled
                       .Where(predicate)
                       .Where(x => after is not ulong a || x.Id > a)
                       .Take(amount)
                       .ToArray();

                if (!msgs.Any())
                    return PruneResult.Success;

                lastMessage = msgs[^1];

                var bulkDeletable = new List<IMessage>();
                var singleDeletable = new List<IMessage>();
                foreach (var x in msgs)
                {
                    _logService.AddDeleteIgnore(x.Id);

                    if (now - x.CreatedAt < _twoWeeks)
                        bulkDeletable.Add(x);
                    else
                        singleDeletable.Add(x);
                }

                if (channel is ITextChannel tc2 && bulkDeletable.Count > 0)
                {
                    await tc2.DeleteMessagesAsync(bulkDeletable);
                    amount -= msgs.Length;
                    progress.Report((originalAmount - amount, originalAmount));
                    await Task.Delay(2000, cancelSource.Token);
                }

                foreach (var group in singleDeletable.Chunk(5))
                {
                    await group.Select(x => x.DeleteAsync()).WhenAll();
                    amount -= 5;
                    progress.Report((originalAmount - amount, originalAmount));
                    await Task.Delay(5000, cancelSource.Token);
                }
            }
        }
        catch
        {
            //ignore
        }
        finally
        {
            _pruningGuilds.TryRemove(gid, out _);
        }

        return PruneResult.Success;
    }

    public async Task<bool> CancelAsync(ulong guildId)
    {
        if (!_pruningGuilds.TryRemove(guildId, out var source))
            return false;

        await source.CancelAsync();
        return true;
    }
}