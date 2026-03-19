using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Services;

namespace NadekoBot.Common.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class OwnerOnlyAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
    {
        var bss = services.GetRequiredService<BotConfigService>();
        if (bss.Data.CommandOverrides.ContainsKey(command.Name.ToLowerInvariant()))
            return Task.FromResult(PreconditionResult.FromSuccess());

        var permService = services.GetRequiredService<IDiscordPermOverrideService>();
        if (permService.TryGetOverrides(context.Guild?.Id ?? 0, command.Name, out _))
            return Task.FromResult(PreconditionResult.FromSuccess());

        var creds = services.GetRequiredService<IBotCredsProvider>().GetCreds();

        return Task.FromResult(creds.IsOwner(context.User) || context.Client.CurrentUser.Id == context.User.Id
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError("Not owner"));
    }
}
