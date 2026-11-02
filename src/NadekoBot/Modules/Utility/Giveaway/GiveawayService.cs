using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.UserNotifications;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace NadekoBot.Modules.Utility;

public sealed class GiveawayService : INService, IReadyExecutor
{
    public static string GiveawayEmoji = "🎉";

    private const string NOTIFY_GIVEAWAY_WON = "giveaway.won";
    public const int MAX_WINNERS = 20;

    private readonly DbService _db;
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;
    private readonly IMessageSenderService _sender;
    private readonly IBotStrings _strings;
    private readonly ILocalization _localization;
    private readonly IMemoryCache _cache;
    private readonly UserNotifyService? _userNotify;
    private static readonly Comparer<GiveawayModel> _endsAtComparer = Comparer<GiveawayModel>.Create(static (x, y) =>
    {
        var cmp = x.EndsAt.CompareTo(y.EndsAt);
        return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
    });

    private SortedSet<GiveawayModel> _giveawayCache = new(_endsAtComparer);

    // message id -> giveaway id, so a reaction on any other message costs no database read
    private readonly ConcurrentDictionary<ulong, int> _giveawayMessages = new();
    private volatile bool _loaded;
    private readonly Lock _giveawayLock = new();
    private readonly NadekoRandom _rng;

    public GiveawayService(
        DbService db,
        IBotCreds creds,
        DiscordSocketClient client,
        IMessageSenderService sender,
        IBotStrings strings,
        ILocalization localization,
        IMemoryCache cache,
        UserNotifyService? userNotify = null)
    {
        _db = db;
        _creds = creds;
        _client = client;
        _sender = sender;
        _strings = strings;
        _localization = localization;
        _cache = cache;
        _userNotify = userNotify;
        _rng = new NadekoRandom();


        _client.ReactionAdded += OnReactionAdded;
        _client.ReactionRemoved += OnReactionRemoved;
    }

    private async Task OnReactionRemoved(
        Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> arg2,
        SocketReaction r)
    {
        if (!r.User.IsSpecified)
            return;

        var user = r.User.Value;

        if (user.IsBot || user.IsWebhook)
            return;

        if (r.Emote is Emoji e && e.Name == GiveawayEmoji && IsGiveawayMessage(msg.Id))
        {
            await LeaveGivawayAsync(msg.Id, user.Id);
        }
    }

    // until the first load is done, every message may be a giveaway
    private bool IsGiveawayMessage(ulong messageId)
        => !_loaded || _giveawayMessages.ContainsKey(messageId);

    private async Task OnReactionAdded(
        Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> ch,
        SocketReaction r)
    {
        if (!r.User.IsSpecified)
            return;

        var user = r.User.Value;

        if (user.IsBot || user.IsWebhook)
            return;

        var textChannel = ch.Value as ITextChannel;

        if (textChannel is null)
            return;


        if (r.Emote is Emoji e && e.Name == GiveawayEmoji && IsGiveawayMessage(msg.Id))
        {
            await JoinGivawayAsync(msg.Id, user.Id, user.Username);
        }
    }

    public async Task OnReadyAsync()
    {
        // load giveaways for this shard from the database
        GiveawayModel[] gas;
        await using (var ctx = _db.GetDbContext())
        {
            gas = await ctx
                        .GetTable<GiveawayModel>()
                        .Where(Queries.GuildOnShard<GiveawayModel>(x => x.GuildId, _creds.TotalShards, _client.ShardId))
                        .ToArrayAsync();
        }

        lock (_giveawayLock)
        {
            _giveawayCache = new(gas, _endsAtComparer);
        }

        foreach (var ga in gas)
            _giveawayMessages[ga.MessageId] = ga.Id;

        _loaded = true;

        _ = Task.Run(EndLoopInternalAsync);
    }

    private async Task EndLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync())
        {
            IEnumerable<GiveawayModel> toEnd;
            lock (_giveawayLock)
            {
                toEnd = _giveawayCache.TakeWhile(
                                          x => x.EndsAt <= DateTime.UtcNow.AddSeconds(15))
                                      .ToArray();
            }

            foreach (var ga in toEnd)
            {
                try
                {
                    await EndGiveawayAsync(ga.GuildId, ga.Id);
                }
                catch
                {
                    Log.Warning("Failed to end the giveaway with id {Id}", ga.Id);
                }
            }
        }
    }

    public async Task<int?> StartGiveawayAsync(
        ulong guildId,
        ulong channelId,
        ulong messageId,
        TimeSpan duration,
        string message,
        int winnerCount = 1)
    {
        await using var ctx = _db.GetDbContext();

        // first check if there are more than 5 giveaways
        var count = await ctx
                          .GetTable<GiveawayModel>()
                          .CountAsync(x => x.GuildId == guildId);

        if (count >= 5)
            return null;

        var endsAt = DateTime.UtcNow + duration;
        var ga = await ctx.GetTable<GiveawayModel>()
                          .InsertWithOutputAsync(() => new GiveawayModel
                          {
                              GuildId = guildId,
                              MessageId = messageId,
                              ChannelId = channelId,
                              Message = message,
                              EndsAt = endsAt,
                              WinnerCount = Math.Clamp(winnerCount, 1, MAX_WINNERS),
                          });

        lock (_giveawayLock)
        {
            _giveawayCache.Add(ga);
        }

        _giveawayMessages[ga.MessageId] = ga.Id;

        return ga.Id;
    }


    public async Task<bool> EndGiveawayAsync(ulong guildId, int id)
    {
        await using var ctx = _db.GetDbContext();

        var giveaway = await ctx
                             .GetTable<GiveawayModel>()
                             .Where(x => x.GuildId == guildId && x.Id == id)
                             .LoadWith(x => x.Participants)
                             .FirstOrDefaultAsyncLinqToDB();

        if (giveaway is null)
            return false;

        // the timer and .ga end can race; only the call which deletes the row ends the giveaway
        var deleted = await ctx
                            .GetTable<GiveawayModel>()
                            .Where(x => x.Id == id)
                            .DeleteAsync();

        if (deleted == 0)
            return false;

        lock (_giveawayLock)
        {
            _giveawayCache.Remove(giveaway);
        }

        _giveawayMessages.TryRemove(giveaway.MessageId, out _);

        var winners = PickWinners(giveaway, Math.Clamp(giveaway.WinnerCount, 1, MAX_WINNERS));

        await OnGiveawayEnded(giveaway, winners);

        return true;
    }

    private GiveawayUser[] PickWinners(GiveawayModel giveaway, int count)
    {
        var participants = giveaway.Participants as List<GiveawayUser> ?? giveaway.Participants.ToList();
        var winners = PickWinners(participants, count, _rng);

        if (participants.Count == 0)
            _cache.Remove($"reroll:{giveaway.Id}");
        else
            _cache.Set($"reroll:{giveaway.Id}", new GiveawayRerollData(giveaway), TimeSpan.FromDays(1));

        giveaway.Participants = participants;
        return winners;
    }

    // removes the winners from the participants, so a reroll can only pick someone else
    public static GiveawayUser[] PickWinners(List<GiveawayUser> participants, int count, NadekoRandom rng)
    {
        var n = Math.Min(Math.Max(count, 1), participants.Count);
        if (n == 0)
            return [];

        var span = CollectionsMarshal.AsSpan(participants);
        for (var i = 0; i < n; i++)
        {
            var j = rng.Next(i, span.Length);
            (span[i], span[j]) = (span[j], span[i]);
        }

        var winners = span[..n].ToArray();
        participants.RemoveRange(0, n);
        return winners;
    }

    public static bool TryParseWinnerCount(ReadOnlySpan<char> token, out int count)
    {
        count = 0;
        return token.Length > 1
               && (token[^1] == 'w' || token[^1] == 'W')
               && int.TryParse(token[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out count)
               && count is >= 1 and <= MAX_WINNERS;
    }

    public async Task<bool> RerollGiveawayAsync(ulong guildId, int giveawayId)
    {
        var rerollModel = _cache.Get<GiveawayRerollData>("reroll:" + giveawayId);

        if (rerollModel is null || rerollModel.Giveaway.GuildId != guildId)
            return false;

        var winners = PickWinners(rerollModel.Giveaway, 1);

        if (winners.Length > 0)
            await OnGiveawayEnded(rerollModel.Giveaway, winners, isReroll: true);

        return true;
    }

    public async Task<bool> CancelGiveawayAsync(ulong guildId, int id)
    {
        await using var ctx = _db.GetDbContext();

        var ga = await ctx
                       .GetTable<GiveawayModel>()
                       .Where(x => x.GuildId == guildId && x.Id == id)
                       .DeleteWithOutputAsync();

        if (ga is not { Length: > 0 })
            return false;

        lock (_giveawayLock)
        {
            _giveawayCache.Remove(ga[0]);
        }

        _giveawayMessages.TryRemove(ga[0].MessageId, out _);

        return true;
    }

    public async Task<IReadOnlyCollection<GiveawayModel>> GetGiveawaysAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();

        return await ctx
                     .GetTable<GiveawayModel>()
                     .Where(x => x.GuildId == guildId)
                     .ToListAsync();
    }

    public async Task<bool> JoinGivawayAsync(ulong messageId, ulong userId, string userName)
    {
        await using var ctx = _db.GetDbContext();

        var giveaway = await ctx
                             .GetTable<GiveawayModel>()
                             .Where(x => x.MessageId == messageId)
                             .FirstOrDefaultAsyncLinqToDB();

        if (giveaway is null)
            return false;

        // add the user to the database
        await ctx.GetTable<GiveawayUser>()
                 .InsertAsync(
                     () => new GiveawayUser()
                     {
                         UserId = userId,
                         GiveawayId = giveaway.Id,
                         Name = userName,
                     }
                 );

        return true;
    }

    public async Task<bool> LeaveGivawayAsync(ulong messageId, ulong userId)
    {
        await using var ctx = _db.GetDbContext();

        var giveaway = await ctx
                             .GetTable<GiveawayModel>()
                             .Where(x => x.MessageId == messageId)
                             .FirstOrDefaultAsyncLinqToDB();

        if (giveaway is null)
            return false;

        await ctx
              .GetTable<GiveawayUser>()
              .Where(x => x.UserId == userId && x.GiveawayId == giveaway.Id)
              .DeleteAsync();

        return true;
    }

    public async Task OnGiveawayEnded(GiveawayModel ga, GiveawayUser[] winners, bool isReroll = false)
    {
        var culture = _localization.GetCultureInfo(ga.GuildId);

        string GetText(LocStr str)
            => _strings.GetText(str, culture);

        var ch = _client.GetChannel(ga.ChannelId) as ITextChannel;
        if (ch is null)
            return;

        var msg = await ch.GetMessageAsync(ga.MessageId) as IUserMessage;
        if (msg is null)
            return;

        var winnerStr = winners.Length switch
        {
            0 => "-",
            1 => $"""
                  {winners[0].Name}
                  <@{winners[0].UserId}>
                  {Format.Code(winners[0].UserId.ToString())}
                  """,
            // names and ids of 20 winners would not fit the 1024 character field limit
            _ => FormatMentions(winners, '\n')
        };

        var eb = _sender.CreateEmbed(ch.GuildId)
                        .WithOkColor()
                        .WithTitle(GetText(strs.giveaway_ended))
                        .WithDescription(ga.Message)
                        .WithFooter($"id: {new kwum(ga.Id).ToString()}")
                        .AddField(winners.Length == 1 ? GetText(strs.winner) : GetText(strs.winners_count),
                            winnerStr,
                            winners.Length <= 1);

        try
        {
            // one rerolled winner must not replace the list of the other winners
            if (!isReroll || ga.WinnerCount <= 1)
                await msg.ModifyAsync(x => x.Embed = eb.Build());

            if (winners.Length > 0)
                await _sender.Response(ch).Message(msg).Text("🎉 " + FormatMentions(winners, ' ')).SendAsync();
        }
        catch
        {
            _ = msg.DeleteAsync();
            await _sender.Response(ch).Embed(eb).SendAsync();
        }

        if (winners.Length > 0 && _userNotify is not null)
            _ = Task.Run(() => NotifyWinnersInternalAsync(_userNotify, ga.Message, winners));
    }

    private async Task NotifyWinnersInternalAsync(UserNotifyService userNotify, string prize, GiveawayUser[] winners)
    {
        var msgStr = strs.giveaway_dm_won(prize.TrimTo(200));
        var notifyEmbed = new EmbedBuilder()
            .WithColor(new Discord.Color(0xF1, 0xC4, 0x0F))
            .WithTitle("Giveaway Won!")
            .WithDescription(_strings.GetText(msgStr.Key, (ulong?)null, msgStr.Params));

        foreach (var winner in winners)
        {
            try
            {
                await userNotify.NotifyAsync(winner.UserId, NOTIFY_GIVEAWAY_WON, notifyEmbed);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to send giveaway won notification");
            }
        }
    }

    private static string FormatMentions(GiveawayUser[] winners, char separator)
    {
        var sb = new StringBuilder(winners.Length * 24);
        foreach (var w in winners)
        {
            if (sb.Length > 0)
                sb.Append(separator);
            sb.Append("<@").Append(w.UserId).Append('>');
        }

        return sb.ToString();
    }
}

public sealed class GiveawayRerollData
{
    public GiveawayModel Giveaway { get; init; }
    public DateTime ExpiresAt { get; init; }

    public GiveawayRerollData(GiveawayModel ga)
    {
        Giveaway = ga;

        ExpiresAt = DateTime.UtcNow.AddDays(1);
    }
}
