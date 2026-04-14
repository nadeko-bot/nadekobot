#nullable disable
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Services;

namespace NadekoBot.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
[SuppressMessage("Style", "IDE0022:Use expression body for methods")]
public sealed class OnlyPublicBotAttribute : PreconditionAttribute
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

#if GLOBAL_NADEKO || DEBUG
        return Task.FromResult(PreconditionResult.FromSuccess());
#else
        return Task.FromResult(PreconditionResult.FromError("Only available on the public bot."));
#endif
    }
}
