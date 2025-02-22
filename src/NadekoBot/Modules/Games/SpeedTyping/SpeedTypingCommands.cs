#nullable disable
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class SpeedTypingCommands : NadekoModule<GamesService>
    {
        private readonly GamesService _games;
        private readonly DiscordSocketClient _client;

        public SpeedTypingCommands(DiscordSocketClient client, GamesService games)
        {
            _games = games;
            _client = client;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<TypingGame.Options>]
        public async Task TypeStart(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new TypingGame.Options(), args);
            var channel = (ITextChannel)ctx.Channel;

            var game = _service.RunningContests.GetOrAdd(ctx.Guild.Id,
                _ => new(_games, _client, channel, prefix, options, _sender));

            if (game.IsActive)
                await Response().Error($"Contest already running in {game.Channel.Mention} channel.").SendAsync();
            else
                await game.Start();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task TypeStop()
        {
            if (_service.RunningContests.TryRemove(ctx.Guild.Id, out var game))
            {
                await game.Stop();
                return;
            }

            await Response().Error("No contest to stop on this channel.").SendAsync();
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task Typeadd([Leftover] string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            _games.AddTypingArticle(ctx.User, text);

            await Response().Confirm("Added new article for typing game.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Typelist(int page = 1)
        {
            if (page < 1)
                return;

            var articles = _games.TypingArticles.Skip((page - 1) * 15).Take(15).ToArray();

            if (!articles.Any())
            {
                await Response().Error($"{ctx.User.Mention} `No articles found on that page.`").SendAsync();
                return;
            }

            var i = (page - 1) * 15;
            await Response()
                  .Confirm("List of articles for Type Race",
                      string.Join("\n", articles.Select(a => $"`#{++i}` - {a.Text.TrimTo(50)}")))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task Typedel(int index)
        {
            var removed = _service.RemoveTypingArticle(--index);

            if (removed is null)
                return;

            var embed = CreateEmbed()
                           .WithTitle($"Removed typing article #{index + 1}")
                           .WithDescription(removed.Text.TrimTo(50))
                           .WithOkColor();

            await Response().Embed(embed).SendAsync();
        }
    }
}