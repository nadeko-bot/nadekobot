using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility;

public sealed class AfkService : INService, IReadyExecutor
{
    private readonly IBotCache _cache;
    private readonly DiscordSocketClient _client;
    private readonly MessageSenderService _mss;

    private static readonly TimeSpan _maxAfkDuration = 8.Hours();

    public AfkService(IBotCache cache, DiscordSocketClient client, MessageSenderService mss)
    {
        _cache = cache;
        _client = client;
        _mss = mss;
    }

    private static TypedKey<string> GetKey(ulong userId)
        => new($"afk:msg:{userId}");

    private static TypedKey<bool> GetRecentlySentKey(ulong userId, ulong channelId)
        => new($"afk:recent:{userId}:{channelId}");

    public async Task<bool> SetAfkAsync(ulong userId, string text)
    {
        var added = await _cache.AddAsync(GetKey(userId), text, _maxAfkDuration, overwrite: true);

        async Task StopAfk(SocketMessage socketMessage)
        {
            try
            {
                if (socketMessage.Author?.Id == userId)
                {
                    await _cache.RemoveAsync(GetKey(userId));
                    _client.MessageReceived -= StopAfk;

                    // write the message saying afk status cleared

                    if (socketMessage.Channel is ITextChannel tc)
                    {
                        _ = Task.Run(async () =>
                        {
                            var msg = await _mss.Response(tc).Confirm("AFK message cleared!").SendAsync();

                            msg.DeleteAfter(5);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Unexpected error occurred while trying to stop afk: {Message}", ex.Message);
            }
        }

        _client.MessageReceived += StopAfk;


        _ = Task.Run(async () =>
        {
            await Task.Delay(_maxAfkDuration);
            _client.MessageReceived -= StopAfk;
        });

        return added;
    }

    public Task OnReadyAsync()
    {
        _client.MessageReceived += TryTriggerAfkMessage;

        return Task.CompletedTask;
    }

    private Task TryTriggerAfkMessage(SocketMessage sm)
    {
        if (sm.Author.IsBot || sm.Author.IsWebhook)
            return Task.CompletedTask;

        if (sm is not IUserMessage uMsg || uMsg.Channel is not ITextChannel tc)
            return Task.CompletedTask;

        if ((sm.MentionedUsers.Count is 0 or > 3) && uMsg.ReferencedMessage is null)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            ulong mentionedUserId = 0;

            if (sm.MentionedUsers.Count <= 3)
            {
                foreach (var uid in uMsg.MentionedUserIds)
                {
                    if (uid == sm.Author.Id)
                        continue;

                    if (sm.Content.StartsWith($"<@{uid}>") || sm.Content.StartsWith($"<@!{uid}>"))
                    {
                        mentionedUserId = uid;
                        break;
                    }
                }
            }

            if (mentionedUserId == 0)
            {
                if (uMsg.ReferencedMessage?.Author?.Id is not ulong repliedUserId)
                {
                    return;
                }

                mentionedUserId = repliedUserId;
            }


            try
            {
                var result = await _cache.GetAsync(GetKey(mentionedUserId));
                if (result.TryPickT0(out var msg, out _))
                {
                    var st = SmartText.CreateFrom(msg);

                    st = $"The user you've pinged (<#{mentionedUserId}>) is AFK: " + st;

                    var toDelete = await _mss.Response(sm.Channel)
                                             .User(sm.Author)
                                             .Message(uMsg)
                                             .Text(st)
                                             .SendAsync();

                    toDelete.DeleteAfter(30);

                    var botUser = await tc.Guild.GetCurrentUserAsync();
                    var perms = botUser.GetPermissions(tc);
                    if (!perms.SendMessages)
                        return;

                    var key = GetRecentlySentKey(mentionedUserId, sm.Channel.Id);
                    var recent = await _cache.GetAsync(key);

                    if (!recent.TryPickT0(out _, out _))
                    {
                        var chMsg = await _mss.Response(sm.Channel)
                                              .Message(uMsg)
                                              .Pending(strs.user_afk($"<@{mentionedUserId}>"))
                                              .SendAsync();

                        chMsg.DeleteAfter(5);
                        await _cache.AddAsync(key, true, expiry: TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (HttpException ex)
            {
                Log.Warning("Error in afk service: {Message}", ex.Message);
            }
        });

        return Task.CompletedTask;
    }
}