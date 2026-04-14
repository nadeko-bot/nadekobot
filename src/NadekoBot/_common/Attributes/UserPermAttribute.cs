using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Services;

namespace Discord;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class UserPermAttribute : RequireUserPermissionAttribute
{
    public UserPermAttribute(GuildPerm permission)
        : base(permission)
    {
    }

    public UserPermAttribute(ChannelPerm permission)
        : base(permission)
    {
    }

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

        return base.CheckPermissionsAsync(context, command, services);
    }
}
