using NadekoBot.Common.TypeReaders;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Administration.Services;

public sealed partial class MessageLinkTypeReader : NadekoTypeReader<MessageLink>
{
    [GeneratedRegex(@"https://discord.com/channels/(?<sid>(?:\d+|@me))/(?<cid>\d{16,19})/(?<mid>\d{16,19})")]
    private partial Regex MessageLinkRegex();

    public override async ValueTask<TypeReaderResult<MessageLink>> ReadAsync(ICommandContext ctx, string input)
    {
        var match = MessageLinkRegex().Match(input);

        if (!match.Success)
            return TypeReaderResult.FromError<MessageLink>(CommandError.ParseFailed, "Invalid message link");

        ulong? guildId = ulong.TryParse(match.Groups["sid"].ToString(), out var sid)
            ? sid
            : null;

        if (guildId != ctx.Guild?.Id)
            return TypeReaderResult.FromError<MessageLink>(CommandError.ParseFailed,
                "Invalid message link. You may only link message from the same server.");

        var channelId = ulong.Parse(match.Groups["cid"].ToString());
        var messageId = ulong.Parse(match.Groups["mid"].ToString());

        var channel = await ctx.Client.GetChannelAsync(channelId) as IMessageChannel;

        if (channel is null)
            return TypeReaderResult.FromError<MessageLink>(CommandError.ParseFailed, "Channel not found");

        var msg = await channel.GetMessageAsync(messageId);

        if (msg is null)
            return TypeReaderResult.FromError<MessageLink>(CommandError.ParseFailed, "Message not found");

        return TypeReaderResult.FromSuccess(new MessageLink(ctx.Guild,
            channel,
            msg
        ));
    }
}