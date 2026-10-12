using Microsoft.Extensions.DependencyInjection;

namespace NadekoBot.Common.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoManagedBotAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
        => Task.FromResult(services.GetRequiredService<BotPolicy>().Managed
            ? PreconditionResult.FromError("Not available on a managed bot.")
            : PreconditionResult.FromSuccess());
}
