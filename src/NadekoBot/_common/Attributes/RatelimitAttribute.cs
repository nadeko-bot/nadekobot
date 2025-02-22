using Microsoft.Extensions.DependencyInjection;

namespace NadekoBot.Common.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RatelimitAttribute : PreconditionAttribute
{
    public int Seconds { get; }

    public RatelimitAttribute(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        Seconds = seconds;
    }

    public override async Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
    {
        if (Seconds == 0)
            return PreconditionResult.FromSuccess();

        var cache = services.GetRequiredService<IBotCache>();
        var rem = await cache.GetRatelimitAsync(
            new($"precondition:{context.User.Id}:{command.Name}"),
            Seconds.Seconds());

        if (rem is null)
            return PreconditionResult.FromSuccess();

        var msgContent = $"You can use this command again in {rem.Value.TotalSeconds:F1}s.";

        return PreconditionResult.FromError(msgContent);
    }
}