#nullable disable
using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Services;

namespace NadekoBot.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoPublicBotAttribute : PreconditionAttribute
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

#if GLOBAL_NADEKO
        return Task.FromResult(PreconditionResult.FromError("Not available on the public bot. To learn how to selfhost a private bot, click [here](https://docs.nadeko.bot)."));
#else
        return Task.FromResult(PreconditionResult.FromSuccess());
#endif
    }
}
