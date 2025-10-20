using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility;

public sealed class StarboardService : INService, IReadyExecutor
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IMessageSenderService _sender;

    public StarboardService(DbService db, DiscordSocketClient client, IMessageSenderService sender)
    {
        _db = db;
        _client = client;
        _sender = sender;

        _client.ReactionAdded += OnReactionAdded;
        _client.ReactionRemoved += OnReactionRemoved;
    }

    public Task OnReadyAsync()
        => Task.CompletedTask;

    private Task OnReactionRemoved(Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> ch,
        SocketReaction r)
        => HandleReactionChanged(msg, ch, r, -1);

    private Task OnReactionAdded(Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> ch,
        SocketReaction r)
        => HandleReactionChanged(msg, ch, r, +1);

    private async Task HandleReactionChanged(Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> ch,
        SocketReaction r,
        int delta)
    {
        if (!r.User.IsSpecified)
            return;

        var user = r.User.Value;
        if (user.IsBot || user.IsWebhook)
            return;

        if (ch.Value is not ITextChannel textCh)
            return;

        await using var ctx = _db.GetDbContext();

        // load guild settings
        var sb = await ctx.Set<StarboardSetting>()
            .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == textCh.GuildId);

        if (sb is null || !sb.IsEnabled || sb.StarboardChannelId is null)
            return;

        // ignore configured channels
        var ignored = await ctx.Set<StarboardIgnoredChannel>()
            .AnyAsyncLinqToDB(x => x.GuildId == textCh.GuildId && x.ChannelId == textCh.Id);

        if (ignored)
            return;

        // emoji filter
        if (sb.StrictEmoji)
        {
            var target = (sb.Emoji ?? "⭐").ToIEmote();
            if (!Emote.TryParse(r.Emote.ToString(), out var e) || e.ToString() != target.ToString())
            {
                if (r.Emote is Emoji ee)
                {
                    if (ee.ToString() != target.ToString())
                        return;
                }
                else
                    return;
            }
        }

        var sourceMessage = await msg.GetOrDownloadAsync();
        if (sourceMessage is null)
            return;

        if (!sb.AllowSelfStar && sourceMessage.Author.Id == user.Id)
            return;

        if (!sb.AllowBotMessages && (sourceMessage.Author.IsBot || sourceMessage.Author.IsWebhook))
            return;

        // per-channel override
        var overrideThreshold = await ctx.Set<StarboardChannelOverride>()
            .Where(x => x.GuildId == textCh.GuildId && x.ChannelId == textCh.Id)
            .Select(x => x.Threshold)
            .FirstOrDefaultAsyncLinqToDB();

        var threshold = overrideThreshold ?? sb.Threshold;

        // count reactions for that emoji
        int count;
        try
        {
            var detailed = await sourceMessage.GetReactionUsersAsync(r.Emote, 100).FlattenAsync();
            count = detailed.Count();
        }
        catch
        {
            // fallback approximate
            count = sourceMessage.Reactions.TryGetValue(r.Emote, out var meta) ? meta.ReactionCount : 0;
        }

        // upsert tracked message
        var tracked = await ctx.Set<StarboardMessage>()
            .FirstOrDefaultAsyncLinqToDB(x => x.SourceMessageId == sourceMessage.Id);

        if (tracked is null)
        {
            tracked = await ctx.Set<StarboardMessage>()
                .InsertWithOutputAsync(() => new StarboardMessage
                {
                    GuildId = textCh.GuildId,
                    ChannelId = textCh.Id,
                    SourceMessageId = sourceMessage.Id,
                    StarCount = count,
                    AuthorId = sourceMessage.Author.Id,
                    SnapshotContent = sourceMessage.Content?.TrimTo(2000)
                });
        }
        else
        {
            tracked.StarCount = count;
            await ctx.UpdateAsync(tracked);
        }

        // post or update starboard entry
        if (count >= threshold)
        {
            var sbChannel = _client.GetChannel(sb.StarboardChannelId.Value) as ITextChannel;
            if (sbChannel is null)
                return;

            var eb = _sender.CreateEmbed(textCh.GuildId)
                .WithOkColor()
                .WithAuthor(sourceMessage.Author)
                .WithDescription(string.IsNullOrWhiteSpace(sourceMessage.Content) ? "-" : sourceMessage.Content)
                .AddField("Jump", sourceMessage.GetJumpUrl())
                .WithFooter($"{(sb.Emoji ?? "⭐")} {count} • #{textCh.Name}");

            if (sourceMessage.Attachments.FirstOrDefault() is { } att && att.Height is not null)
                eb.WithImageUrl(att.Url);

            if (tracked.StarboardMessageId is null)
            {
                var posted = await _sender.Response(sbChannel).Embed(eb).SendAsync();
                tracked.StarboardMessageId = posted.Id;
                await ctx.UpdateAsync(tracked);
            }
            else
            {
                try
                {
                    var msg2 = await sbChannel.GetMessageAsync(tracked.StarboardMessageId.Value) as IUserMessage;
                    if (msg2 is not null)
                        await msg2.ModifyAsync(m => m.Embed = eb.Build());
                }
                catch { }
            }
        }
        else if (tracked?.StarboardMessageId is not null)
        {
            // below threshold - delete starboard message if exists
            var sbChannel = _client.GetChannel(sb.StarboardChannelId.Value) as ITextChannel;
            if (sbChannel is not null)
            {
                try
                {
                    var msg2 = await sbChannel.GetMessageAsync(tracked.StarboardMessageId.Value) as IUserMessage;
                    if (msg2 is not null)
                        await msg2.DeleteAsync();
                }
                catch { }
            }

            tracked.StarboardMessageId = null;
            await ctx.UpdateAsync(tracked);
        }
    }
}
