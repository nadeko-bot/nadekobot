#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Common;

namespace NadekoBot.Modules.Searches.Services;

public sealed class StreamOnlineMessageDeleterService : INService, IReadyExecutor
{
    private readonly StreamNotificationService _notifService;
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IPubSub _pubSub;

    public StreamOnlineMessageDeleterService(
        StreamNotificationService notifService,
        DbService db,
        IPubSub pubSub,
        DiscordSocketClient client)
    {
        _notifService = notifService;
        _db = db;
        _client = client;
        _pubSub = pubSub;
    }

    public async Task OnReadyAsync()
    {
        _notifService.OnlineMessagesSent += OnOnlineMessagesSent;

        if (_client.ShardId == 0)
            await _pubSub.Sub(_notifService.StreamsOfflineKey, OnStreamsOffline);
    }

    private async Task OnOnlineMessagesSent(
        FollowedStream.FType type,
        string name,
        IReadOnlyCollection<(ulong, ulong)> pairs)
    {
        await using var ctx = _db.GetDbContext();
        foreach (var (channelId, messageId) in pairs)
        {
            await ctx.GetTable<StreamOnlineMessage>()
                     .InsertAsync(() => new()
                     {
                         Name = name,
                         Type = type,
                         MessageId = messageId,
                         ChannelId = channelId,
                         DateAdded = DateTime.UtcNow,
                     });
        }
    }

    private async ValueTask OnStreamsOffline(List<StreamData> streamDatas)
    {
        if (_client.ShardId != 0)
            return;

        var pairs = await GetMessagesToDelete(streamDatas);

        foreach (var (channelId, messageId) in pairs)
        {
            try
            {
                var textChannel = await _client.GetChannelAsync(channelId) as ITextChannel;
                if (textChannel is null)
                    continue;

                await textChannel.DeleteMessageAsync(messageId);
            }
            catch
            {
                continue;
            }
        }
    }

    private async Task<IEnumerable<(ulong, ulong)>> GetMessagesToDelete(List<StreamData> streamDatas)
    {
        await using var ctx = _db.GetDbContext();

        var toReturn = new List<(ulong, ulong)>();
        foreach (var sd in streamDatas)
        {
            var key = sd.CreateKey();
            var toDelete = await ctx.GetTable<StreamOnlineMessage>()
                                    .Where(x => (x.Type == key.Type && x.Name == key.Name)
                                                || Sql.DateDiff(Sql.DateParts.Day, x.DateAdded, DateTime.UtcNow) > 1)
                                    .DeleteWithOutputAsync();

            toReturn.AddRange(toDelete.Select(x => (x.ChannelId, x.MessageId)));
        }

        return toReturn;
    }
}