#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Collections.Frozen;

namespace NadekoBot.Modules.Searches;

public sealed partial class FlagTranslateService : IReadyExecutor, INService
{
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;
    private readonly TranslateService _ts;
    private readonly IMessageSenderService _sender;
    private IReadOnlyDictionary<string, string> _supportedFlags;
    private readonly DbService _db;
    private ConcurrentHashSet<ulong> _enabledChannels;
    private readonly IBotCache _cache;

    // disallow same message being translated multiple times to the same language
    private readonly ConcurrentHashSet<(ulong, string)> _msgLangs = new();

    public FlagTranslateService(
        IBotCreds creds,
        DiscordSocketClient client,
        TranslateService ts,
        IMessageSenderService sender,
        DbService db,
        IBotCache cache)
    {
        _creds = creds;
        _client = client;
        _ts = ts;
        _sender = sender;
        _db = db;
        _cache = cache;
    }

    public async Task OnReadyAsync()
    {
        _supportedFlags = COUNTRIES
                          .Split('\n')
                          .Select(x => x.Split(' '))
                          .ToDictionary(x => x[0], x => x[1].TrimEnd())
                          .ToFrozenDictionary();

        await using (var uow = _db.GetDbContext())
        {
            _enabledChannels = (await uow.GetTable<FlagTranslateChannel>()
                                         .Where(x => Queries.GuildOnShard(x.GuildId,
                                             _creds.TotalShards,
                                             _client.ShardId))
                                         .Select(x => new
                                         {
                                             x.ChannelId,
                                             x.GuildId
                                         })
                                         .ToListAsyncLinqToDB())
                               .Select(x => x.ChannelId)
                               .ToHashSet()
                               .ToConcurrentSet();
        }

        _client.ReactionAdded += OnReactionAdded;

        var periodicCleanup = new PeriodicTimer(TimeSpan.FromHours(24));

        while (await periodicCleanup.WaitForNextTickAsync())
        {
            _msgLangs.Clear();
        }
    }

    private const int FLAG_START = 127462;

    private static TypedKey<bool> CdKey(ulong userId)
        => new($"flagtranslate:{userId}");

    private Task OnReactionAdded(
        Cacheable<IUserMessage, ulong> arg1,
        Cacheable<IMessageChannel, ulong> arg2,
        SocketReaction reaction)
    {
        if (!_enabledChannels.Contains(reaction.Channel.Id))
            return Task.CompletedTask;

        var runes = reaction.Emote.Name.EnumerateRunes();
        if (!runes.MoveNext()
            || runes.Current is not { Value: >= 127462 and <= 127487 } l1
            || !runes.MoveNext()
            || runes.Current is not { Value: >= 127462 and <= 127487 } l2)
        {
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            if (reaction.Channel is not SocketTextChannel tc)
                return;

            var user = await ((IGuild)tc.Guild).GetUserAsync(reaction.UserId);

            if (user is null)
                return;

            if (!user.GetPermissions(tc).SendMessages)
                return;

            if (!tc.Guild.CurrentUser.GetPermissions(tc).SendMessages
                || !tc.Guild.CurrentUser.GetPermissions(tc).EmbedLinks)
            {
                await Disable(tc.Guild.Id, tc.Id);
                return;
            }

            var c1 = (char)(l1.Value - FLAG_START + 65);
            var c2 = (char)(l2.Value - FLAG_START + 65);

            var code = $"{c1}{c2}".ToUpper();

            if (!_supportedFlags.TryGetValue(code, out var lang))
                return;

            if (_msgLangs.Contains((reaction.MessageId, lang)))
                return;

            var result = await _cache.GetAsync(CdKey(reaction.UserId));
            if (result.TryPickT0(out _, out _))
                return;
            
            if (!_msgLangs.Add((reaction.MessageId, lang)))
                return;

            await _cache.AddAsync(CdKey(reaction.UserId), true, TimeSpan.FromSeconds(5));

            var msg = await arg1.GetOrDownloadAsync();

            var response = await _ts.Translate("", lang, msg.Content).ConfigureAwait(false);

            await msg.ReplyAsync(embed: _sender.CreateEmbed(tc.Guild?.Id)
                                               .WithOkColor()
                                               .WithFooter(user.ToString() ?? reaction.UserId.ToString(),
                                                   user.RealAvatarUrl().ToString())
                                               .WithDescription(response)
                                               .WithAuthor(reaction.Emote.ToString())
                                               .Build(),
                allowedMentions: AllowedMentions.None
            );
        });

        return Task.CompletedTask;
    }

    public async Task Disable(ulong guildId, ulong tcId)
    {
        if (!_enabledChannels.TryRemove(tcId))
            return;

        await using var uow = _db.GetDbContext();
        await uow.GetTable<FlagTranslateChannel>()
                 .Where(x => x.GuildId == guildId
                             && x.ChannelId == tcId)
                 .DeleteAsync();
    }

    public async Task<bool> Toggle(ulong guildId, ulong tcId)
    {
        if (_enabledChannels.Contains(tcId))
        {
            await Disable(guildId, tcId);

            return false;
        }

        await Enable(guildId, tcId);

        return true;
    }

    public async Task Enable(ulong guildId, ulong tcId)
    {
        if (!_enabledChannels.Add(tcId))
            return;

        await using var uow = _db.GetDbContext();
        await uow.GetTable<FlagTranslateChannel>()
                 .InsertAsync(() => new FlagTranslateChannel
                 {
                     GuildId = guildId,
                     ChannelId = tcId
                 });
    }
}