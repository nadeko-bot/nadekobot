using NadekoBot.Modules.Games.Hangman;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class HangmanCommands : NadekoModule<IHangmanService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Hangmanlist()
            => await Response()
                .Confirm(GetText(strs.hangman_types(prefix)), _service.GetHangmanTypes().Join('\n'))
                .SendAsync();

        private static string Draw(HangmanGame.State state)
        {
            var head = state.Errors >= 1 ? (state.Failed ? "X" : "O") : " ";
            var rope = state.Errors >= 1 ? "│" : " ";
            var torso = state.Errors >= 2 ? "|" : " ";
            var leftArm = state.Errors >= 3 ? "/" : " ";
            var rightArm = state.Errors >= 4 ? "\\" : " ";
            var leftLeg = state.Errors >= 5 ? "/" : " ";
            var rightLeg = state.Errors >= 6 ? "\\" : " ";

            return $"""
                    ```
                      ┌──────┐
                      │/     {rope}
                      │      {head}
                      │     {leftArm}{torso}{rightArm}
                      │     {leftLeg} {rightLeg}
                    ──┴──────────
                    ```
                    """;
        }

        public static EmbedBuilder GetEmbed(IMessageSenderService sender, HangmanGame.State state)
        {
            var eb = sender.CreateEmbed()
                .WithOkColor()
                .AddField("Hangman", Draw(state))
                .AddField("Guess", Format.Code(state.Word));

            if (state.Phase == HangmanGame.Phase.Running)
            {
                return eb
                    .WithFooter(state.MissedLetters.Join(' '))
                    .WithAuthor(state.Category);
            }

            if (state.Phase == HangmanGame.Phase.Ended && state.Failed)
            {
                return eb
                    .WithFooter(state.MissedLetters.Join(' '));
            }

            return eb.WithFooter(state.MissedLetters.Join(' '));
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Hangman([Leftover] string? type = null)
        {
            if (await RefuseWithoutMessageContentAsync("hangman"))
                return;

            var (result, hangman) = await _service.StartHangmanAsync(ctx.Guild.Id, ctx.Channel.Id, type);
            if (result == HangmanStartResult.NoTerm)
            {
                await Response()
                    .Error(GetText(strs.hangman_types(prefix)), _service.GetHangmanTypes().Join('\n'))
                    .SendAsync();
                return;
            }

            if (result == HangmanStartResult.LimitReached)
            {
                await Response()
                    .Error(strs.hangman_limit(HangmanService.MAX_GAMES_PER_SERVER, prefix + "hangmanstop"))
                    .SendAsync();
                return;
            }

            if (hangman is null)
            {
                await Response().Error(strs.hangman_running).SendAsync();
                return;
            }

            var eb = GetEmbed(_sender, hangman);
            eb.WithDescription(GetText(strs.hangman_game_started));

            IUserMessage sent;
            try
            {
                sent = await Response().Embed(eb).SendAsync();
            }
            catch
            {
                // nobody sees a game without its board, so it must not hold a place of the server limit
                await _service.StopHangman(ctx.Channel.Id);
                throw;
            }

            _service.SetLastMessage(ctx.Channel.Id, sent);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task HangmanStop()
        {
            if (await _service.StopHangman(ctx.Channel.Id))
                await Response().Confirm(strs.hangman_stopped).SendAsync();
        }
    }
}