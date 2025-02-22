using MessageType = Discord.MessageType;

namespace NadekoBot.Modules.Administration;

public sealed class DoAsUserMessage : IUserMessage
{
    private readonly string _message;
    private IUserMessage _msg;
    private readonly IUser _user;

    public DoAsUserMessage(SocketUserMessage msg, IUser user, string message)
    {
        _msg = msg;
        _user = user;
        _message = message;
    }

    public ulong Id
        => _msg.Id;

    public DateTimeOffset CreatedAt
        => _msg.CreatedAt;

    public Task DeleteAsync(RequestOptions? options = null)
    {
        return _msg.DeleteAsync(options);
    }

    public Task AddReactionAsync(IEmote emote, RequestOptions? options = null)
    {
        return _msg.AddReactionAsync(emote, options);
    }

    public Task RemoveReactionAsync(IEmote emote, IUser user, RequestOptions? options = null)
    {
        return _msg.RemoveReactionAsync(emote, user, options);
    }

    public Task RemoveReactionAsync(IEmote emote, ulong userId, RequestOptions? options = null)
    {
        return _msg.RemoveReactionAsync(emote, userId, options);
    }

    public Task RemoveAllReactionsAsync(RequestOptions? options = null)
    {
        return _msg.RemoveAllReactionsAsync(options);
    }

    public Task RemoveAllReactionsForEmoteAsync(IEmote emote, RequestOptions? options = null)
    {
        return _msg.RemoveAllReactionsForEmoteAsync(emote, options);
    }

    public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(
        IEmote emoji,
        int limit,
        RequestOptions? options = null,
        ReactionType type = ReactionType.Normal)
        => _msg.GetReactionUsersAsync(emoji, limit, options, type);

    public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(
        IEmote emoji,
        int limit,
        RequestOptions? options = null)
    {
        return _msg.GetReactionUsersAsync(emoji, limit, options);
    }

    public MessageType Type
        => _msg.Type;

    public MessageSource Source
        => _msg.Source;

    public bool IsTTS
        => _msg.IsTTS;

    public bool IsPinned
        => _msg.IsPinned;

    public bool IsSuppressed
        => _msg.IsSuppressed;

    public bool MentionedEveryone
        => _msg.MentionedEveryone;

    public string Content
        => _message;

    public string CleanContent
        => _msg.CleanContent;

    public DateTimeOffset Timestamp
        => _msg.Timestamp;

    public DateTimeOffset? EditedTimestamp
        => _msg.EditedTimestamp;

    public IMessageChannel Channel
        => _msg.Channel;

    public IUser Author
        => _user;

    public IThreadChannel Thread
        => _msg.Thread;

    public IReadOnlyCollection<IAttachment> Attachments
        => _msg.Attachments;

    public IReadOnlyCollection<IEmbed> Embeds
        => _msg.Embeds;

    public IReadOnlyCollection<ITag> Tags
        => _msg.Tags;

    public IReadOnlyCollection<ulong> MentionedChannelIds
        => _msg.MentionedChannelIds;

    public IReadOnlyCollection<ulong> MentionedRoleIds
        => _msg.MentionedRoleIds;

    public IReadOnlyCollection<ulong> MentionedUserIds
        => _msg.MentionedUserIds;

    public MessageActivity Activity
        => _msg.Activity;

    public MessageApplication Application
        => _msg.Application;

    public MessageReference Reference
        => _msg.Reference;

    public IReadOnlyDictionary<IEmote, ReactionMetadata> Reactions
        => _msg.Reactions;

    public IReadOnlyCollection<IMessageComponent> Components
        => _msg.Components;

    public IReadOnlyCollection<IStickerItem> Stickers
        => _msg.Stickers;

    public MessageFlags? Flags
        => _msg.Flags;

    [Obsolete("Obsolete in favor of InteractionMetadata")]
    public IMessageInteraction Interaction
        => _msg.Interaction;

    public MessageRoleSubscriptionData RoleSubscriptionData
        => _msg.RoleSubscriptionData;

    public PurchaseNotification PurchaseNotification
        => _msg.PurchaseNotification;

    public MessageCallData? CallData
        => _msg.CallData;

    public IReadOnlyCollection<MessageSnapshot> ForwardedMessages
        => _msg.ForwardedMessages;

    public Task ModifyAsync(Action<MessageProperties> func, RequestOptions? options = null)
    {
        return _msg.ModifyAsync(func, options);
    }

    public Task PinAsync(RequestOptions? options = null)
    {
        return _msg.PinAsync(options);
    }

    public Task UnpinAsync(RequestOptions? options = null)
    {
        return _msg.UnpinAsync(options);
    }

    public Task CrosspostAsync(RequestOptions? options = null)
    {
        return _msg.CrosspostAsync(options);
    }

    public string Resolve(
        TagHandling userHandling = TagHandling.Name,
        TagHandling channelHandling = TagHandling.Name,
        TagHandling roleHandling = TagHandling.Name,
        TagHandling everyoneHandling = TagHandling.Ignore,
        TagHandling emojiHandling = TagHandling.Name)
    {
        return _msg.Resolve(userHandling, channelHandling, roleHandling, everyoneHandling, emojiHandling);
    }

    public Task EndPollAsync(RequestOptions options)
        => _msg.EndPollAsync(options);

    public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetPollAnswerVotersAsync(
        uint answerId,
        int? limit = null,
        ulong? afterId = null,
        RequestOptions? options = null)
        => _msg.GetPollAnswerVotersAsync(
            answerId,
            limit,
            afterId,
            options);

    public MessageResolvedData ResolvedData
        => _msg.ResolvedData;

    public IUserMessage ReferencedMessage
        => _msg.ReferencedMessage;

    public IMessageInteractionMetadata InteractionMetadata
        => _msg.InteractionMetadata;

    public Poll? Poll
        => _msg.Poll;
}