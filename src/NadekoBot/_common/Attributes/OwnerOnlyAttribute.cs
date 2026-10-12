using NadekoBot.Modules.Administration;
using Microsoft.Extensions.DependencyInjection;

namespace NadekoBot.Common.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class OwnerOnlyAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
    {
        if (context.Message is DoAsUserMessage { BlocksOwnerCommands: true })
            return Task.FromResult(PreconditionResult.FromError("Owner commands cannot run on behalf of the owner"));

        var creds = services.GetRequiredService<IBotCredsProvider>().GetCreds();

        return Task.FromResult(creds.IsOwner(context.User) || context.Client.CurrentUser.Id == context.User.Id
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError("Not owner"));
    }
}