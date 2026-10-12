#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Discord;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Modules.Utility.AiAgent.Adapters;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class AiMessagingToolTests
{
    private const ulong CHANNEL_ID = 10;
    private const ulong BOT_ID = 20;
    private const ulong MESSAGE_ID = 30;

    [Test]
    public async Task EditAndDelete_NeedManageMessagesExceptForMessagesTheSessionSent()
    {
        var adapter = new MessagingAiAdapter(Substitute.For<IMessageSenderService>());
        var member = CreateContext(manageMessages: false);

        var editDenied = Assert.ThrowsAsync<ToolException>(() => adapter.EditMessage(member, CHANNEL_ID, MESSAGE_ID, "x"));
        Assert.That(editDenied!.Code, Is.EqualTo("missing_permission"));
        var deleteDenied = Assert.ThrowsAsync<ToolException>(() => adapter.DeleteMessage(member, CHANNEL_ID, MESSAGE_ID));
        Assert.That(deleteDenied!.Code, Is.EqualTo("missing_permission"));

        member.SentMessageIds[MESSAGE_ID] = true;
        Assert.That(await adapter.EditMessage(member, CHANNEL_ID, MESSAGE_ID, "x"), Is.EqualTo("Message edited successfully."));
        Assert.That(await adapter.DeleteMessage(member, CHANNEL_ID, MESSAGE_ID), Is.EqualTo("Message deleted successfully."));

        var moderator = CreateContext(manageMessages: true);
        Assert.That(await adapter.EditMessage(moderator, CHANNEL_ID, MESSAGE_ID, "x"), Is.EqualTo("Message edited successfully."));
        Assert.That(await adapter.DeleteMessage(moderator, CHANNEL_ID, MESSAGE_ID), Is.EqualTo("Message deleted successfully."));
    }

    private static AiToolContext CreateContext(bool manageMessages)
    {
        var bot = Substitute.For<IGuildUser>();
        bot.Id.Returns(BOT_ID);

        var msg = Substitute.For<IUserMessage>();
        msg.Id.Returns(MESSAGE_ID);
        msg.Author.Returns(bot);

        var channel = Substitute.For<ITextChannel>();
        channel.Id.Returns(CHANNEL_ID);
        channel.GetMessageAsync(MESSAGE_ID, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(msg);

        var guild = Substitute.For<IGuild>();
        guild.GetTextChannelAsync(CHANNEL_ID, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(channel);
        guild.GetCurrentUserAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(bot);

        var user = Substitute.For<IGuildUser>();
        user.GetPermissions(channel)
            .Returns(new ChannelPermissions(viewChannel: true,
                sendMessages: true,
                readMessageHistory: true,
                manageMessages: manageMessages));

        return new AiToolContext
        {
            Guild = guild,
            SourceChannel = channel,
            User = user,
            TriggerMessage = Substitute.For<IUserMessage>(),
            CancellationToken = CancellationToken.None
        };
    }
}
