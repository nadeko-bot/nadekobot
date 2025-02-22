using Discord;

namespace NadekoBot.Medusa;

public static class MedusaExtensions
{
    public static Task<IUserMessage> EmbedAsync(this IMessageChannel ch, EmbedBuilder embed, string msg = "")
        => ch.SendMessageAsync(msg,
            embed: embed.Build(),
            options: new()
            {
                RetryMode = RetryMode.Retry502
            });

    // unlocalized
    public static Task<IUserMessage> SendConfirmAsync(this AnyContext ctx, string msg)
        => ctx.Channel.EmbedAsync(new EmbedBuilder()
                           .WithColor(0, 200, 0)
                           .WithDescription(msg));

    public static Task<IUserMessage> SendPendingAsync(this AnyContext ctx, string msg)
        => ctx.Channel.EmbedAsync(new EmbedBuilder()
                           .WithColor(200, 200, 0)
                           .WithDescription(msg));

    public static Task<IUserMessage> SendErrorAsync(this AnyContext ctx, string msg)
        => ctx.Channel.EmbedAsync(new EmbedBuilder()
                           .WithColor(200, 0, 0)
                           .WithDescription(msg));

    // localized
    public static Task ConfirmAsync(this AnyContext ctx)
        => ctx.Message.AddReactionAsync(new Emoji("✅"));

    public static Task ErrorAsync(this AnyContext ctx)
        => ctx.Message.AddReactionAsync(new Emoji("❌"));

    public static Task WarningAsync(this AnyContext ctx)
        => ctx.Message.AddReactionAsync(new Emoji("⚠️"));

    public static Task WaitAsync(this AnyContext ctx)
        => ctx.Message.AddReactionAsync(new Emoji("🤔"));

    public static Task<IUserMessage> ErrorLocalizedAsync(this AnyContext ctx, string key, params object[]? args)
        => ctx.SendErrorAsync(ctx.GetText(key, args));

    public static Task<IUserMessage> PendingLocalizedAsync(this AnyContext ctx, string key, params object[]? args)
        => ctx.SendPendingAsync(ctx.GetText(key, args));

    public static Task<IUserMessage> ConfirmLocalizedAsync(this AnyContext ctx, string key, params object[]? args)
        => ctx.SendConfirmAsync(ctx.GetText(key, args));

    public static Task<IUserMessage> ReplyErrorLocalizedAsync(this AnyContext ctx, string key, params object[]? args)
        => ctx.SendErrorAsync($"{Format.Bold(ctx.User.ToString())} {ctx.GetText(key, args)}");

    public static Task<IUserMessage> ReplyPendingLocalizedAsync(this AnyContext ctx, string key, params object[]? args)
        => ctx.SendPendingAsync($"{Format.Bold(ctx.User.ToString())} {ctx.GetText(key, args)}");

    public static Task<IUserMessage> ReplyConfirmLocalizedAsync(this AnyContext ctx, string key, params object[]? args)
        => ctx.SendConfirmAsync($"{Format.Bold(ctx.User.ToString())} {ctx.GetText(key, args)}");
}