namespace NadekoBot.Modules.Utility.AiAgent;

// Checked before the Discord call, because a 403 counts toward the invalid request limit.
public static class AgentPermissions
{
    // A member who cannot see a channel learns nothing about it, not even that it exists.
    public static ToolException ChannelNotFound()
        => ToolException.NotFound("Channel not found.");

    // A thread has no overwrites of its own, so its permissions come from the parent; without a cached parent nothing is allowed.
    public static ChannelPermissions PermissionsIn(IGuildUser user, ITextChannel channel)
        => channel switch
        {
            SocketThreadChannel { ParentChannel: { } parent } => user.GetPermissions(parent),
            IThreadChannel => ChannelPermissions.None,
            _ => user.GetPermissions(channel)
        };

    // Discord refuses each message action of a member with a timeout, also of the bot.
    public static bool IsTimedOut(IGuildUser user)
        => user.TimedOutUntil is { } until && until > DateTimeOffset.UtcNow;

    public static bool CanSend(IGuildUser bot, ITextChannel channel)
        => CanSend(bot, channel, PermissionsIn(bot, channel));

    // A locked thread takes messages only from members with Manage Threads.
    public static bool CanSend(IGuildUser bot, ITextChannel channel, ChannelPermissions perms)
        => perms.ViewChannel
           && (channel is IThreadChannel thread
               ? perms.SendMessagesInThreads && (!thread.IsLocked || perms.ManageThreads)
               : perms.SendMessages)
           && !IsTimedOut(bot);

    // A private thread shows only to its members and to Manage Threads.
    public static AgentChannelAccess? Access(IGuildUser member, IGuildUser bot, ITextChannel channel, ulong sourceChannelId)
    {
        var botTimedOut = IsTimedOut(bot);
        if (channel is not IThreadChannel thread)
        {
            var memberPerms = member.GetPermissions(channel);
            return memberPerms.ViewChannel
                ? new(memberPerms, bot.GetPermissions(channel), false, false, false, false, botTimedOut)
                : null;
        }

        if (channel is not SocketThreadChannel { ParentChannel: { } parent } socketThread)
            return null;

        var memberInParent = member.GetPermissions(parent);
        if (!memberInParent.ViewChannel)
            return null;

        // GetUser reads only the cache, which lists a member after Discord sent them; others see the thread from inside it.
        var isPrivate = thread.Type == ThreadType.PrivateThread;
        if (isPrivate
            && channel.Id != sourceChannelId
            && !memberInParent.ManageThreads
            && socketThread.GetUser(member.Id) is null)
            return null;

        var botInParent = bot.GetPermissions(parent);
        var botOutside = isPrivate && !socketThread.HasJoined && !botInParent.ManageThreads;
        return new(memberInParent, botInParent, true, thread.IsLocked, thread.IsArchived, botOutside, botTimedOut);
    }

    public static ToolException? CheckRead(in AgentChannelAccess access)
    {
        if (!access.Member.ReadMessageHistory)
            return Member("The member cannot read the message history of that channel.");

        if (access.BotOutside)
            return Bot(BOT_OUTSIDE);

        if (!access.Bot.ViewChannel || !access.Bot.ReadMessageHistory)
            return Bot("The bot needs View Channel and Read Message History in that channel. A server admin can give them.");

        return null;
    }

    // Slow mode limits the member, so the bot does not post there for them unless slow mode does not apply to them.
    public static ToolException? CheckSend(in AgentChannelAccess access, bool slowMode, bool embeds)
    {
        var member = access.Member;
        if (!(access.Thread ? member.SendMessagesInThreads : member.SendMessages))
            return Member(access.Thread ? "The member cannot send messages in that thread." : "The member cannot send messages in that channel.");

        if (access.Locked && !member.ManageThreads)
            return Member("That thread is locked. Only members with Manage Threads can post there.");

        if (slowMode && !member.ManageMessages && !member.ManageChannel)
            return Member("That channel has slow mode, so the member must post there themselves.");

        if (embeds && !member.EmbedLinks)
            return Member("The member cannot send embeds in that channel.");

        var bot = access.Bot;
        if (access.BotOutside)
            return Bot(BOT_OUTSIDE);

        if (access.BotTimedOut)
            return Bot(BOT_TIMED_OUT);

        if (!bot.ViewChannel || !(access.Thread ? bot.SendMessagesInThreads : bot.SendMessages))
            return Bot(access.Thread
                ? "The bot needs View Channel and Send Messages in Threads there. A server admin can give them."
                : "The bot needs View Channel and Send Messages in that channel. A server admin can give them.");

        if (access.Locked && !bot.ManageThreads)
            return Bot("That thread is locked, and the bot does not have Manage Threads there.");

        if (embeds && !bot.EmbedLinks)
            return Bot("The bot needs Embed Links in that channel. Send plain text instead.");

        return null;
    }

    public static ToolException? CheckEdit(in AgentChannelAccess access, bool sentForMember)
    {
        // A bot message can be a greet, a reaction role panel or the result of another member's command.
        if (!sentForMember && !access.Member.ManageMessages)
            return Member("The bot edits only its own messages. Without Manage Messages, the member can edit only a message "
                          + "which you sent for them in this conversation.");

        return CheckFind(access, sentForMember);
    }

    // Before the fetch: a member who cannot read the history learns nothing about the message ids there.
    public static ToolException? CheckFind(in AgentChannelAccess access, bool sentForMember)
    {
        if (!sentForMember && !access.Member.ReadMessageHistory)
            return Member("The member cannot read the message history of that channel.");

        return CheckChangeInternal(access);
    }

    public static ToolException? CheckDelete(
        in AgentChannelAccess access,
        bool authoredByMember,
        bool authoredByBot,
        bool sentForMember)
    {
        var own = authoredByMember || (sentForMember && authoredByBot);
        if (!own && !access.Member.ManageMessages)
            return Member("The member needs Manage Messages to delete this message.");

        if (!authoredByBot && !access.Bot.ManageMessages)
            return Bot("The bot needs Manage Messages in that channel to delete a message which it did not write.");

        return null;
    }

    private const string BOT_OUTSIDE = "The bot is not in that private thread. A member of the thread can add it.";
    private const string BOT_TIMED_OUT = "The bot has a timeout in this server, so it cannot post, edit or delete messages now.";

    private static ToolException? CheckChangeInternal(in AgentChannelAccess access)
    {
        if (access.BotOutside)
            return Bot(BOT_OUTSIDE);

        if (access.BotTimedOut)
            return Bot(BOT_TIMED_OUT);

        if (!access.Bot.ViewChannel || !access.Bot.ReadMessageHistory)
            return Bot("The bot needs View Channel and Read Message History in that channel. A server admin can give them.");

        if (access.Archived)
            return ToolException.Forbidden("That thread is archived. Discord does not allow edits or deletes in it.");

        return null;
    }

    private static ToolException Member(string reason)
        => ToolException.MissingPermission(reason);

    private static ToolException Bot(string reason)
        => new("bot_missing_permission", reason);
}

public readonly record struct AgentChannelAccess(
    ChannelPermissions Member,
    ChannelPermissions Bot,
    bool Thread,
    bool Locked,
    bool Archived,
    bool BotOutside,
    bool BotTimedOut);
