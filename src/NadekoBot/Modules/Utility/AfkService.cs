using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility;

public sealed class AfkService(
    IBotCache cache,
    DiscordSocketClient client,
    MessageSenderService mss,
    IBotStrings bs)
    : INService, IReadyExecutor
{
    public const int MAX_TEXT_LENGTH = 200;
    private const int MAX_MENTIONS = 3;

    private static readonly TimeSpan _maxAfkDuration = 8.Hours();
    private static readonly TimeSpan _replyCooldown = TimeSpan.FromMinutes(5);

    private static TypedKey<string> GetKey(ulong userId)
        => new($"afk:msg:{userId}");

    private static TypedKey<bool> GetRecentlySentKey(ulong userId, ulong channelId)
        => new($"afk:recent:{userId}:{channelId}");

    // the set time is stored with the text, so the message which ran .afk can't clear it again
    public static string Encode(DateTimeOffset setAt, string? text)
        => $"{setAt.ToUnixTimeMilliseconds()}|{text}";

    public static (DateTimeOffset SetAt, string? Text) Decode(string value)
    {
        var sep = value.IndexOf('|');
        if (sep < 0 || !long.TryParse(value.AsSpan(0, sep), out var ms))
            return (DateTimeOffset.MinValue, value);

        var text = value[(sep + 1)..];
        return (DateTimeOffset.FromUnixTimeMilliseconds(ms), text.Length == 0 ? null : text);
    }

    public async Task<bool> SetAfkAsync(ulong userId, DateTimeOffset setAt, string? text)
        => await cache.AddAsync(GetKey(userId), Encode(setAt, text), _maxAfkDuration, overwrite: true);

    public Task OnReadyAsync()
    {
        client.MessageReceived += OnMessageReceivedAsync;
        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(SocketMessage sm)
    {
        if (sm.Author.IsBot || sm.Author.IsWebhook)
            return Task.CompletedTask;

        if (sm is not IUserMessage uMsg || uMsg.Channel is not ITextChannel tc)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            await TryClearSelfAfkInternalAsync(uMsg, tc);
            await TryReplyAfkOnMentionInternalAsync(uMsg, tc);
        });

        return Task.CompletedTask;
    }

    private async Task TryClearSelfAfkInternalAsync(IUserMessage msg, ITextChannel tc)
    {
        try
        {
            var key = GetKey(msg.Author.Id);
            var result = await cache.GetAsync(key);
            if (!result.TryPickT0(out var value, out _))
                return;

            if (msg.CreatedAt <= Decode(value).SetAt)
                return;

            await cache.RemoveAsync(key);

            if (!CanSend(tc))
                return;

            var reply = await mss.Response(tc).Confirm(strs.afk_cleared).SendAsync();
            reply.DeleteAfter(5);
        }
        catch (Exception ex)
        {
            Log.Warning("Unexpected error clearing afk: {Message}", ex.Message);
        }
    }

    private async Task TryReplyAfkOnMentionInternalAsync(IUserMessage msg, ITextChannel tc)
    {
        var mentionedUserId = GetMentionedUserId(msg);
        if (mentionedUserId == 0)
            return;

        try
        {
            var result = await cache.GetAsync(GetKey(mentionedUserId));
            if (!result.TryPickT0(out var value, out _))
                return;

            if (!CanSend(tc))
                return;

            // one reply per afk user per channel, so pinging someone repeatedly doesn't make the bot spam
            var recentKey = GetRecentlySentKey(mentionedUserId, tc.Id);
            if (!await cache.AddAsync(recentKey, true, _replyCooldown, overwrite: false))
                return;

            var (_, text) = Decode(value);

            if (text is null)
            {
                var noReason = strs.afk_no_reason;
                text = bs.GetText(noReason.Key, tc.GuildId, noReason.Params);
            }

            // an embed, because mentions in embeds never ping
            var reply = await mss.Response(tc)
                .Message(msg)
                .Pending(strs.afk_reply($"<@{mentionedUserId}>", text))
                .SendAsync();

            reply.DeleteAfter(30);
        }
        catch (HttpException ex)
        {
            Log.Warning("Error in afk service: {Message}", ex.Message);
        }
    }

    private static ulong GetMentionedUserId(IUserMessage msg)
    {
        var mentions = msg.MentionedUserIds;
        if (mentions.Count is > 0 and <= MAX_MENTIONS)
        {
            var content = msg.Content.AsSpan();
            foreach (var uid in mentions)
            {
                if (uid == msg.Author.Id)
                    continue;

                if (StartsWithMention(content, uid))
                    return uid;
            }
        }

        if (msg.ReferencedMessage?.Author?.Id is ulong repliedUserId && repliedUserId != msg.Author.Id)
            return repliedUserId;

        return 0;
    }

    private static bool StartsWithMention(ReadOnlySpan<char> content, ulong userId)
    {
        if (!content.StartsWith("<@"))
            return false;

        content = content[2..];
        if (content.StartsWith("!"))
            content = content[1..];

        var end = content.IndexOf('>');
        return end > 0 && ulong.TryParse(content[..end], out var id) && id == userId;
    }

    private static bool CanSend(ITextChannel tc)
        => tc is SocketGuildChannel sgc
           && sgc.Guild.CurrentUser.GetPermissions(sgc) is { ViewChannel: true, SendMessages: true, EmbedLinks: true };
}
