using NadekoBot.AiAgent;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Services;

namespace NadekoBot.Modules.Administration;

public sealed class ServerConfigAiAdapter(
    ICommandHandler commandHandler,
    ILocalization localization,
    GreetService greet) : IAiToolGroup, INService
{
    public string GroupName => "server_config";
    public string GroupDescription => "Server-wide configuration: command prefix, locale, greet/bye messages.";

    [AiTool("get_server_prefix", "Returns the bot command prefix configured for this server.")]
    public Task<ServerPrefixDto> GetServerPrefix(AiToolContext ctx)
        => Task.FromResult(new ServerPrefixDto(ctx.Guild.Id, commandHandler.GetPrefix(ctx.Guild)));

    [AiTool("get_server_locale", "Returns the locale/culture configured for this server, which determines language and formatting.")]
    public Task<ServerLocaleDto> GetServerLocale(AiToolContext ctx)
    {
        var ci = localization.GetCultureInfo(ctx.Guild);
        var hasGuildOverride = localization.GuildCultureInfos.ContainsKey(ctx.Guild.Id);
        return Task.FromResult(new ServerLocaleDto(
            ctx.Guild.Id,
            ci.Name,
            ci.EnglishName,
            hasGuildOverride,
            localization.DefaultCultureInfo.Name));
    }

    [AiTool("get_greet_settings", "Returns the greet/bye/boost message settings for this server.")]
    public async Task<GreetSettingsDto> GetGreetSettings(AiToolContext ctx)
    {
        var greetCfg = await greet.GetGreetSettingsAsync(ctx.Guild.Id, GreetType.Greet);
        var greetDmCfg = await greet.GetGreetSettingsAsync(ctx.Guild.Id, GreetType.GreetDm);
        var byeCfg = await greet.GetGreetSettingsAsync(ctx.Guild.Id, GreetType.Bye);
        var boostCfg = await greet.GetGreetSettingsAsync(ctx.Guild.Id, GreetType.Boost);

        return new(
            ctx.Guild.Id,
            ToDtoInternal(greetCfg),
            ToDtoInternal(greetDmCfg),
            ToDtoInternal(byeCfg),
            ToDtoInternal(boostCfg));
    }

    private static GreetEntryDto? ToDtoInternal(GreetSettings? cfg)
        => cfg is null
            ? null
            : new GreetEntryDto(cfg.IsEnabled, cfg.ChannelId, cfg.MessageText, cfg.AutoDeleteTimer);
}

public readonly record struct ServerPrefixDto(ulong GuildId, string Prefix);

public readonly record struct ServerLocaleDto(
    ulong GuildId,
    string Locale,
    string EnglishName,
    bool HasGuildOverride,
    string DefaultLocale);

public sealed record GreetSettingsDto(
    ulong GuildId,
    GreetEntryDto? Greet,
    GreetEntryDto? GreetDm,
    GreetEntryDto? Bye,
    GreetEntryDto? Boost);

public readonly record struct GreetEntryDto(
    bool Enabled,
    ulong? ChannelId,
    string? Message,
    int AutoDeleteTimerSeconds);
