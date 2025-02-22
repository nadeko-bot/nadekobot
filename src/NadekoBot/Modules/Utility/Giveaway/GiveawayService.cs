using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public sealed class GiveawayService : INService, IReadyExecutor
{
    public static string GiveawayEmoji = "🎉";

    private readonly DbService _db;
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;
    private readonly IMessageSenderService _sender;
    private readonly IBotStrings _strings;
    private readonly ILocalization _localization;
    private readonly IMemoryCache _cache;
    private SortedSet<GiveawayModel> _giveawayCache = new SortedSet<GiveawayModel>();
    private readonly NadekoRandom _rng;

    public GiveawayService(
        DbService db,
        IBotCreds creds,
        DiscordSocketClient client,
        IMessageSenderService sender,
        IBotStrings strings,
        ILocalization localization,
        IMemoryCache cache)
    {
        _db = db;
        _creds = creds;
        _client = client;
        _sender = sender;
        _strings = strings;
        _localization = localization;
        _cache = cache;
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

        if (r.Emote is Emoji e && e.Name == GiveawayEmoji)
        {
            await LeaveGivawayAsync(msg.Id, user.Id);
        }
    }

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


        if (r.Emote is Emoji e && e.Name == GiveawayEmoji)
        {
            await JoinGivawayAsync(msg.Id, user.Id, user.Username);
        }
    }

    public async Task OnReadyAsync()
    {
        // load giveaways for this shard from the database
        await using var ctx = _db.GetDbContext();

        var gas = await ctx
                        .GetTable<GiveawayModel>()
                        .Where(x => Queries.GuildOnShard(x.GuildId, _creds.TotalShards, _client.ShardId))
                        .ToArrayAsync();

        lock (_giveawayCache)
        {
            _giveawayCache = new(gas, Comparer<GiveawayModel>.Create((x, y) => x.EndsAt.CompareTo(y.EndsAt)));
        }

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync())
        {
            IEnumerable<GiveawayModel> toEnd;
            lock (_giveawayCache)
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
        string message)
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
                          });

        lock (_giveawayCache)
        {
            _giveawayCache.Add(ga);
        }

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

        await ctx
              .GetTable<GiveawayModel>()
              .Where(x => x.Id == id)
              .DeleteAsync();

        lock (_giveawayCache)
        {
            _giveawayCache.Remove(giveaway);
        }

        var winner = PickWinner(giveaway);

        await OnGiveawayEnded(giveaway, winner);

        return true;
    }

    private GiveawayUser? PickWinner(GiveawayModel giveaway)
    {
        if (giveaway.Participants.Count == 0)
            return default;

        if (giveaway.Participants.Count == 1)
        {
            // as this is the last participant, rerolls no longer possible
            _cache.Remove($"reroll:{giveaway.Id}");
            return giveaway.Participants[0];
        }

        var winner = giveaway.Participants[_rng.Next(0, giveaway.Participants.Count - 1)];

        HandleWinnerSelection(giveaway, winner);
        return winner;
    }

    public async Task<bool> RerollGiveawayAsync(ulong guildId, int giveawayId)
    {
        var rerollModel = _cache.Get<GiveawayRerollData>("reroll:" + giveawayId);

        if (rerollModel is null)
            return false;

        var winner = PickWinner(rerollModel.Giveaway);

        if (winner is not null)
        {
            await OnGiveawayEnded(rerollModel.Giveaway, winner);
        }

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

        lock (_giveawayCache)
        {
            _giveawayCache.Remove(ga[0]);
        }

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

    public async Task OnGiveawayEnded(GiveawayModel ga, GiveawayUser? winner)
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

        var winnerStr = winner is null
            ? "-"
            : $"""
               {winner.Name}
               <@{winner.UserId}>
               {Format.Code(winner.UserId.ToString())}
               """;

        var eb = _sender.CreateEmbed(ch.GuildId)
                        .WithOkColor()
                        .WithTitle(GetText(strs.giveaway_ended))
                        .WithDescription(ga.Message)
                        .WithFooter($"id: {new kwum(ga.Id).ToString()}")
                        .AddField(GetText(strs.winner),
                            winnerStr,
                            true);

        try
        {
            await msg.ModifyAsync(x => x.Embed = eb.Build());

            if (winner is not null)
                await _sender.Response(ch).Message(msg).Text($"🎉 <@{winner.UserId}>").SendAsync();
        }
        catch
        {
            _ = msg.DeleteAsync();
            await _sender.Response(ch).Embed(eb).SendAsync();
        }
    }

    private void HandleWinnerSelection(GiveawayModel ga, GiveawayUser winner)
    {
        ga.Participants = ga.Participants.Where(x => x.UserId != winner.UserId).ToList();

        var rerollData = new GiveawayRerollData(ga);
        _cache.Set($"reroll:{ga.Id}", rerollData, TimeSpan.FromDays(1));
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