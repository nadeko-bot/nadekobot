using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NadekoBot;
using NadekoBot.Modules.Administration.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class MessageLinkTypeReaderTests
{
    private const ulong GUILD_ID = 1000000000000000001;
    private const ulong OWN_CHANNEL_ID = 2000000000000000001;
    private const ulong FOREIGN_CHANNEL_ID = 2000000000000000002;
    private const ulong ARCHIVED_THREAD_ID = 2000000000000000003;
    private const ulong OTHER_GUILD_ID = 1000000000000000002;
    private const ulong MESSAGE_ID = 3000000000000000001;

    private static string Link(ulong channelId)
        => $"https://discord.com/channels/{GUILD_ID}/{channelId}/{MESSAGE_ID}";

    [Test]
    public async Task ResolvesOnlyChannelsOfTheCurrentServerIncludingUncached()
    {
        var msg = Substitute.For<IUserMessage>();
        msg.Id.Returns(MESSAGE_ID);

        var ownChannel = Substitute.For<ITextChannel>();
        ownChannel.Id.Returns(OWN_CHANNEL_ID);
        ownChannel.GetMessageAsync(MESSAGE_ID).Returns(msg);

        ownChannel.GuildId.Returns(GUILD_ID);

        var foreignChannel = Substitute.For<ITextChannel>();
        foreignChannel.Id.Returns(FOREIGN_CHANNEL_ID);
        foreignChannel.GuildId.Returns(OTHER_GUILD_ID);
        foreignChannel.GetMessageAsync(MESSAGE_ID).Returns(msg);

        // not in the server's cache, only the client can fetch it
        var archivedThread = Substitute.For<IThreadChannel>();
        archivedThread.Id.Returns(ARCHIVED_THREAD_ID);
        archivedThread.GuildId.Returns(GUILD_ID);
        archivedThread.GetMessageAsync(MESSAGE_ID).Returns(msg);

        var guild = Substitute.For<IGuild>();
        guild.Id.Returns(GUILD_ID);

        var client = Substitute.For<IDiscordClient>();
        client.GetChannelAsync(FOREIGN_CHANNEL_ID).Returns(foreignChannel);
        client.GetChannelAsync(OWN_CHANNEL_ID).Returns(ownChannel);
        client.GetChannelAsync(ARCHIVED_THREAD_ID).Returns(archivedThread);

        var ctx = Substitute.For<ICommandContext>();
        ctx.Guild.Returns(guild);
        ctx.Client.Returns(client);
        ctx.Channel.Returns(ownChannel);

        var reader = new MessageLinkTypeReader();

        var own = await reader.ReadAsync(ctx, Link(OWN_CHANNEL_ID));
        Assert.That(own.IsSuccess, Is.True);
        var link = (MessageLink)own.Values.First().Value;
        Assert.That(link.Channel.Id, Is.EqualTo(OWN_CHANNEL_ID));

        var thread = await reader.ReadAsync(ctx, Link(ARCHIVED_THREAD_ID));
        Assert.That(thread.IsSuccess, Is.True);

        var foreign = await reader.ReadAsync(ctx, Link(FOREIGN_CHANNEL_ID));
        Assert.That(foreign.IsSuccess, Is.False);
    }
}
