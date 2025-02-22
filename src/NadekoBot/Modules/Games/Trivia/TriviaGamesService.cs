#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Games.Common.Trivia;

namespace NadekoBot.Modules.Games;

public sealed class TriviaGamesService : IReadyExecutor, INService
{
    private readonly DiscordSocketClient _client;
    public ConcurrentDictionary<ulong, TriviaGame> RunningTrivias { get; } = new();

    public TriviaGamesService(DiscordSocketClient client)
    {
        _client = client;
    }
    
    public Task OnReadyAsync()
    {
        _client.MessageReceived += OnMessageReceived;

        return Task.CompletedTask;
    }

    private async Task OnMessageReceived(SocketMessage msg)
    {
        if (msg.Author.IsBot)
            return;

        var umsg = msg as SocketUserMessage;

        if (umsg?.Channel is not IGuildChannel gc)
            return;

        if (RunningTrivias.TryGetValue(gc.GuildId, out var tg))
            await tg.InputAsync(new(umsg.Author.Mention, umsg.Author.Id), umsg.Content);
    }
}