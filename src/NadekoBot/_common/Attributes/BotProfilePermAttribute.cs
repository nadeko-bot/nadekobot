using Microsoft.Extensions.DependencyInjection;

namespace NadekoBot.Common.Attributes;

// In a DM only the real owner passes (global profile). In a server Manage Server passes (the bot's server profile).
[AttributeUsage(AttributeTargets.Method)]
public sealed class BotProfilePermAttribute : PreconditionAttribute
{
    public const string REQUIREMENT = "Bot Owner (DM) / ManageGuild Server Permission (server)";

    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
    {
        if (context.Guild is null)
        {
            var creds = services.GetRequiredService<IBotCredsProvider>().GetCreds();
            return Task.FromResult(BotPolicy.IsRealOwner(creds, context.Message)
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError("Only the bot owner can change the bot's global profile"));
        }

        var permService = services.GetRequiredService<IDiscordPermOverrideService>();
        if (permService.TryGetOverrides(context.Guild.Id, command.PermKey(), out _))
            return Task.FromResult(PreconditionResult.FromSuccess());

        return Task.FromResult(context.User is IGuildUser { GuildPermissions.ManageGuild: true }
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError("User requires guild permission ManageGuild"));
    }
}
