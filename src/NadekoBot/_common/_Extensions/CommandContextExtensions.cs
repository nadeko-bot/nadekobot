namespace NadekoBot.Extensions;

public static class CommandContextExtensions
{
    private static readonly Emoji _okEmoji = new Emoji("✅");
    private static readonly Emoji _warnEmoji = new Emoji("⚠️");
    private static readonly Emoji _errorEmoji = new Emoji("❌");

    public static Task ReactAsync(this ICommandContext ctx, MsgType type)
    {
        var emoji = type switch
        {
            MsgType.Error => _errorEmoji,
            MsgType.Pending => _warnEmoji,
            MsgType.Ok => _okEmoji,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        return ctx.Message.AddReactionAsync(emoji);
    }

    public static Task OkAsync(this ICommandContext ctx)
        => ctx.ReactAsync(MsgType.Ok);

    public static Task ErrorAsync(this ICommandContext ctx)
        => ctx.ReactAsync(MsgType.Error);

    public static Task WarningAsync(this ICommandContext ctx)
        => ctx.ReactAsync(MsgType.Pending);
    
    
    public static Task OkAsync(this IUserMessage msg)
        => msg.AddReactionAsync(_okEmoji);
    
    public static Task ErrorAsync(this IUserMessage msg)
        => msg.AddReactionAsync(_errorEmoji);
    
    public static Task WarningAsync(this IUserMessage msg)
        => msg.AddReactionAsync(_warnEmoji);
}