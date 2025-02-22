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
            => await Response().Confirm(GetText(strs.hangman_types(prefix)), _service.GetHangmanTypes().Join('\n')).SendAsync();

        private static string Draw(HangmanGame.State state)
            => $"""
            . ┌─────┐
            .┃...............┋
            .┃...............┋
            .┃{(state.Errors > 0 ? ".............😲" : "")}
            .┃{(state.Errors > 1 ? "............./" : "")} {(state.Errors > 2 ? "|" : "")} {(state.Errors > 3 ? "\\" : "")}
            .┃{(state.Errors > 4 ? "............../" : "")} {(state.Errors > 5 ? "\\" : "")}
            /-\
            """;

        public static EmbedBuilder GetEmbed(IMessageSenderService sender, HangmanGame.State state)
        {
            if (state.Phase == HangmanGame.Phase.Running)
            {
                return sender.CreateEmbed()
                         .WithOkColor()
                         .AddField("Hangman", Draw(state))
                         .AddField("Guess", Format.Code(state.Word))
                         .WithFooter(state.MissedLetters.Join(' '));
            }

            if (state.Phase == HangmanGame.Phase.Ended && state.Failed)
            {
                return sender.CreateEmbed()
                         .WithErrorColor()
                         .AddField("Hangman", Draw(state))
                         .AddField("Guess", Format.Code(state.Word))
                         .WithFooter(state.MissedLetters.Join(' '));
            }

            return sender.CreateEmbed()
                     .WithOkColor()
                     .AddField("Hangman", Draw(state))
                     .AddField("Guess", Format.Code(state.Word))
                     .WithFooter(state.MissedLetters.Join(' '));
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Hangman([Leftover] string? type = null)
        {
            if (!_service.StartHangman(ctx.Channel.Id, type, out var hangman))
            {
                await Response().Error(strs.hangman_running).SendAsync();
                return;
            }

            var eb = GetEmbed(_sender, hangman);
            eb.WithDescription(GetText(strs.hangman_game_started));
            await Response().Embed(eb).SendAsync();
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