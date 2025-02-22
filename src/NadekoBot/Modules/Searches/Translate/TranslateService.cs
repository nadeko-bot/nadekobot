#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Net;

namespace NadekoBot.Modules.Searches;

public sealed class TranslateService : ITranslateService, IExecNoCommand, IReadyExecutor, INService
{
    private readonly IGoogleApiService _google;
    private readonly DbService _db;
    private readonly IMessageSenderService _sender;
    private readonly IBot _bot;

    private readonly ConcurrentDictionary<ulong, bool> _atcs = new();
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, (string From, string To)>> _users = new();

    public TranslateService(
        IGoogleApiService google,
        DbService db,
        IMessageSenderService sender,
        IBot bot)
    {
        _google = google;
        _db = db;
        _sender = sender;
        _bot = bot;
    }

    public async Task OnReadyAsync()
    {
        List<AutoTranslateChannel> cs;
        await using (var ctx = _db.GetDbContext())
        {
            var guilds = await ctx.GetTable<GuildConfig>()
                                   .Select(x => x.GuildId)
                                   .ToListAsyncLinqToDB();
            cs = await ctx.Set<AutoTranslateChannel>().Include(x => x.Users)
                          .Where(x => guilds.Contains(x.GuildId))
                          .ToListAsyncEF();
        }

        foreach (var c in cs)
        {
            _atcs[c.ChannelId] = c.AutoDelete;
            _users[c.ChannelId] = new(c.Users.ToDictionary(x => x.UserId, x => (x.Source.ToLower(), x.Target.ToLower())));
        }
    }

    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Content))
            return;

        if (msg is { Channel: ITextChannel tch } um)
        {
            if (!_atcs.TryGetValue(tch.Id, out var autoDelete))
                return;

            if (!_users.TryGetValue(tch.Id, out var users) || !users.TryGetValue(um.Author.Id, out var langs))
                return;

            var output = await _google.Translate(msg.Content, langs.From, langs.To);

            if (string.IsNullOrWhiteSpace(output)
                || msg.Content.Equals(output, StringComparison.InvariantCultureIgnoreCase))
                return;

            var embed = _sender.CreateEmbed(guild?.Id).WithOkColor();

            if (autoDelete)
            {
                embed.WithAuthor(um.Author.ToString(), um.Author.GetAvatarUrl())
                     .AddField(langs.From, um.Content)
                     .AddField(langs.To, output);

                await _sender.Response(tch).Embed(embed).SendAsync();

                try
                {
                    await um.DeleteAsync();
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    _atcs.TryUpdate(tch.Id, false, true);
                }

                return;
            }

            await um.ReplyAsync(embed: embed.AddField(langs.To, output).Build(), allowedMentions: AllowedMentions.None);
        }
    }

    public async Task<string> Translate(string source, string target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is empty or null", nameof(text));

        var res = await _google.Translate(text, source.ToLowerInvariant(), target.ToLowerInvariant());
        return res.SanitizeMentions(true);
    }

    public async Task<bool> ToggleAtl(ulong guildId, ulong channelId, bool autoDelete)
    {
        await using var ctx = _db.GetDbContext();

        var old = await ctx.Set<AutoTranslateChannel>()
                           .FirstOrDefaultAsyncEF(x => x.ChannelId == channelId);

        if (old is null)
        {
            ctx.Set<AutoTranslateChannel>().Add(new()
            {
                GuildId = guildId,
                ChannelId = channelId,
                AutoDelete = autoDelete
            });

            await ctx.SaveChangesAsync();

            _atcs[channelId] = autoDelete;
            _users[channelId] = new();

            return true;
        }

        // if autodelete value is different, update the autodelete value
        // instead of disabling
        if (old.AutoDelete != autoDelete)
        {
            old.AutoDelete = autoDelete;
            await ctx.SaveChangesAsync();
            _atcs[channelId] = autoDelete;
            return true;
        }

        await ctx.Set<AutoTranslateChannel>().ToLinqToDBTable().DeleteAsync(x => x.ChannelId == channelId);

        await ctx.SaveChangesAsync();
        _atcs.TryRemove(channelId, out _);
        _users.TryRemove(channelId, out _);

        return false;
    }


    private void UpdateUser(
        ulong channelId,
        ulong userId,
        string from,
        string to)
    {
        var dict = _users.GetOrAdd(channelId, new ConcurrentDictionary<ulong, (string, string)>());
        dict[userId] = (from, to);
    }

    public async Task<bool?> RegisterUserAsync(
        ulong userId,
        ulong channelId,
        string from,
        string to)
    {
        if (!_google.Languages.ContainsKey(from) || !_google.Languages.ContainsKey(to))
            return null;

        await using var ctx = _db.GetDbContext();
        var ch = await ctx.Set<AutoTranslateChannel>().GetByChannelId(channelId);

        if (ch is null)
            return null;

        var user = ch.Users.FirstOrDefault(x => x.UserId == userId);

        if (user is null)
        {
            ch.Users.Add(user = new()
            {
                Source = from,
                Target = to,
                UserId = userId
            });

            await ctx.SaveChangesAsync();

            UpdateUser(channelId, userId, from, to);

            return true;
        }

        // if it's different from old settings, update
        if (user.Source != from || user.Target != to)
        {
            user.Source = from;
            user.Target = to;

            await ctx.SaveChangesAsync();

            UpdateUser(channelId, userId, from, to);

            return true;
        }

        return await UnregisterUser(channelId, userId);
    }

    public async Task<bool> UnregisterUser(ulong channelId, ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        var rows = await ctx.Set<AutoTranslateUser>().ToLinqToDBTable()
                            .DeleteAsync(x => x.UserId == userId && x.Channel.ChannelId == channelId);

        if (_users.TryGetValue(channelId, out var inner))
            inner.TryRemove(userId, out _);

        return rows > 0;
    }

    public IEnumerable<string> GetLanguages()
        => _google.Languages.GroupBy(x => x.Value).Select(x => $"{x.AsEnumerable().Select(y => y.Key).Join(", ")}");
}