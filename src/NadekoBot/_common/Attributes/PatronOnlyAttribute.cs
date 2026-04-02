using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Common.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PatronOnlyAttribute(PatronTier minTier = PatronTier.None) : PreconditionAttribute
{
    public PatronTier MinTier { get; } = minTier;

    public override async Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
    {
        var patronService = services.GetRequiredService<IPatronageService>();
        var config = patronService.GetConfig();

        if (!config.IsEnabled)
            return PreconditionResult.FromSuccess();

        var creds = services.GetRequiredService<IBotCredsProvider>().GetCreds();

        if (creds.IsOwner(context.User))
            return PreconditionResult.FromSuccess();

        var patron = await patronService.GetPatronAsync(context.User.Id);

        if (patron is null || !patron.Value.IsActive)
            return PreconditionResult.FromError("This command requires an active patron subscription.");

        if (MinTier != PatronTier.None && patron.Value.Tier < MinTier)
            return PreconditionResult.FromError($"This command requires Patron Tier {MinTier} or higher.");

        return PreconditionResult.FromSuccess();
    }
}
