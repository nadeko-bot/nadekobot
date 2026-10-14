using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class ApiEndpoints
{
    private const string TEXT_CHANNEL = "text";
    private const string VOICE_CHANNEL = "voice";
    private const string ROLE_COLOR_FORMAT = "x6";
    private const int MEMORY_MB_DECIMALS = 2;

    private static readonly GuildFeature[] _featureFlags = GetSingleFeatureFlagsInternal();
    private static readonly string[] _featureFlagNames = Array.ConvertAll(_featureFlags, static f => f.ToString());

    public enum ChannelKind
    {
        Other,
        Text,
        Voice
    }

    public static RouteGroupBuilder MapOwnerEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/status", GetStatus);
        owner.MapGet("/guilds", GetGuilds);
        owner.MapGet("/configs", GetConfigs);
        owner.MapGet("/configs/{name}", GetConfig);
        owner.MapPut("/configs/{name}/{prop}", SetConfig);
        owner.MapGlobalExpressionEndpoints();
        owner.MapFeedEndpoints();
        owner.MapBackupCreateEndpoint();
        owner.MapAiKeyEndpoints();
        owner.MapOwnersEndpoints();
        return owner;
    }

    public static RouteGroupBuilder MapGuildEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/", GetGuildInfo);
        guild.MapGet("/channels", GetChannels);
        guild.MapGet("/roles", GetRoles);
        guild.MapGreetEndpoints();
        guild.MapExpressionEndpoints();
        guild.MapQuoteEndpoints();
        guild.MapWarnEndpoints();
        guild.MapXpEndpoints();
        guild.MapHealthEndpoints();
        return guild;
    }

    // Guild routes which only standalone mode maps: they create currency, replace data or need the activity counter.
    public static RouteGroupBuilder MapOwnerGuildEndpoints(this RouteGroupBuilder ownerGuild)
    {
        ownerGuild.MapXpCurrencyRewardEndpoints();
        ownerGuild.MapActivityEndpoints();
        ownerGuild.MapServerImportEndpoint();
        return ownerGuild;
    }

    private static IResult GetStatus(ICoordinator coord, IStatsService stats, ShardData shardData)
    {
        var statuses = coord.GetAllShardStatuses();
        var shards = new ShardStatusDto[statuses.Count];
        long guildCount = 0;
        for (var i = 0; i < statuses.Count; i++)
        {
            var s = statuses[i];
            guildCount += s.GuildCount;
            shards[i] = new ShardStatusDto(
                s.ShardId,
                s.ConnectionState.ToString(),
                s.GuildCount);
        }

        var dto = new StatusDto(
            shardData.TotalShards,
            guildCount,
            Math.Round(stats.GetPrivateMemoryMegabytes(), MEMORY_MB_DECIMALS),
            (long)stats.GetUptime().TotalSeconds,
            stats.CommandsRan,
            shards);

        return Results.Json(dto, ApiJsonContext.Default.StatusDto);
    }

    private static IResult GetGuilds(DiscordSocketClient client)
    {
        var guilds = client.Guilds;
        var arr = new GuildListItemDto[guilds.Count];
        var i = 0;
        foreach (var g in guilds)
        {
            if (i == arr.Length)
                break;

            // Discord sent no data for an unavailable guild yet, so its name is null.
            if (!((IGuild)g).Available)
                continue;

            arr[i++] = new GuildListItemDto(
                g.Id.ToString(),
                g.Name,
                g.IconUrl,
                g.MemberCount,
                g.OwnerId.ToString());
        }

        return Results.Json(TrimInternal(arr, i), ApiJsonContext.Default.GuildListItemDtoArray);
    }

    // Built from the gateway cache only, so a dashboard refresh never costs a Discord REST call.
    private static IResult GetGuildInfo(GuildRef guildRef)
    {
        var guild = guildRef.Guild;

        var textCount = 0;
        var voiceCount = 0;
        foreach (var c in guild.Channels)
        {
            switch (Classify(c))
            {
                case ChannelKind.Text:
                    textCount++;
                    break;
                case ChannelKind.Voice:
                    voiceCount++;
                    break;
            }
        }

        var emotes = guild.Emotes;
        var emojis = new EmojiDto[emotes.Count];
        var i = 0;
        foreach (var e in emotes)
        {
            if (i == emojis.Length)
                break;
            emojis[i++] = new EmojiDto(e.Name, e.ToString(), e.Url);
        }

        var dto = new GuildInfoDto(
            guild.Id.ToString(),
            guild.Name,
            guild.IconUrl,
            guild.OwnerId.ToString(),
            guild.Owner?.Username,
            guild.MemberCount,
            textCount,
            voiceCount,
            guild.CreatedAt.UtcDateTime,
            SortedRolesInternal(guild),
            TrimInternal(emojis, i),
            GetFeatureNamesInternal(guild.Features));

        return Results.Json(dto, ApiJsonContext.Default.GuildInfoDto);
    }

    private static IResult GetChannels(GuildRef guild)
    {
        var channels = guild.Guild.Channels;
        var arr = new ChannelDto[channels.Count];
        var i = 0;
        foreach (var c in channels)
        {
            if (i == arr.Length)
                break;

            var kind = Classify(c);
            if (kind != ChannelKind.Other)
                arr[i++] = new ChannelDto(c.Id.ToString(), c.Name, kind == ChannelKind.Text ? TEXT_CHANNEL : VOICE_CHANNEL);
        }

        return Results.Json(TrimInternal(arr, i), ApiJsonContext.Default.ChannelDtoArray);
    }

    // Voice, stage and thread channels all derive from SocketTextChannel, so the order of these cases matters.
    public static ChannelKind Classify(SocketGuildChannel channel)
        => channel switch
        {
            SocketThreadChannel => ChannelKind.Other,
            SocketVoiceChannel => ChannelKind.Voice,
            SocketTextChannel => ChannelKind.Text,
            _ => ChannelKind.Other
        };

    private static GuildFeature[] GetSingleFeatureFlagsInternal()
    {
        var all = Enum.GetValues<GuildFeature>();
        var flags = new GuildFeature[all.Length];
        var n = 0;
        foreach (var f in all)
        {
            if (ulong.IsPow2((ulong)f))
                flags[n++] = f;
        }

        return flags[..n];
    }

    private static string[] GetFeatureNamesInternal(GuildFeatures features)
    {
        var value = features.Value;
        var experimental = features.Experimental ?? [];
        var count = experimental.Count;
        foreach (var f in _featureFlags)
        {
            if ((value & f) != 0)
                count++;
        }

        if (count == 0)
            return [];

        var names = new string[count];
        var i = 0;
        for (var j = 0; j < _featureFlags.Length; j++)
        {
            if ((value & _featureFlags[j]) != 0)
                names[i++] = _featureFlagNames[j];
        }

        foreach (var name in experimental)
        {
            if (i == names.Length)
                break;
            names[i++] = name;
        }

        return TrimInternal(names, i);
    }

    // Gateway collections can change while they are copied, so the copy loops stop at the array end and trim.
    private static T[] TrimInternal<T>(T[] items, int count)
        => count == items.Length ? items : items[..count];

    private static IResult GetRoles(GuildRef guild)
        => Results.Json(SortedRolesInternal(guild.Guild), ApiJsonContext.Default.RoleDtoArray);

    private static RoleDto[] SortedRolesInternal(SocketGuild guild)
    {
        var roles = guild.Roles;
        var arr = new RoleDto[roles.Count];
        // Discord breaks position ties by id, so the order stays the same between requests.
        var keys = new (int NegPosition, ulong Id)[roles.Count];
        var i = 0;
        foreach (var role in roles)
        {
            if (i == arr.Length)
                break;

            keys[i] = (-role.Position, role.Id);
            arr[i++] = ToRoleDtoInternal(role);
        }

        Array.Sort(keys, arr, 0, i);
        return TrimInternal(arr, i);
    }

    private static RoleDto ToRoleDtoInternal(IRole role)
        => new(
            role.Id.ToString(),
            role.Name,
            role.GetIconUrl(),
            role.Color.RawValue.ToString(ROLE_COLOR_FORMAT));

    public static IResult GetConfigs(ConfigRegistry configs)
        => Results.Json(configs.Names, ApiJsonContext.Default.IReadOnlyListString);

    public static IResult GetConfig(string name, ConfigRegistry configs)
    {
        if (!configs.TryGet(name, out var config, out var props))
            return ApiErrors.NotFound;

        return Results.Json(ToConfigDto(config, props), ApiJsonContext.Default.ConfigDto);
    }

    public static IResult SetConfig(string name, string prop, SetConfigRequest body, ConfigRegistry configs)
    {
        if (configs.IsLocked(name, prop))
            return ApiErrors.SettingLocked;

        if (!configs.TryGet(name, out var config, out var props))
            return ApiErrors.NotFound;

        if (!ConfigRegistry.TryGetProp(props, prop, out var canonical))
            return ApiErrors.PropNotFound;

        if (body.Value.Length > ApiLimits.MAX_TEXT_LENGTH || !config.SetSetting(canonical, body.Value))
            return ApiErrors.InvalidValue;

        return Results.Json(ToConfigPropDto(config, canonical), ApiJsonContext.Default.ConfigPropDto);
    }

    public static ConfigDto ToConfigDto(IConfigService config, string[] propNames)
    {
        var props = new ConfigPropDto[propNames.Length];
        for (var i = 0; i < propNames.Length; i++)
            props[i] = ToConfigPropDto(config, propNames[i]);

        return new ConfigDto(config.Name, props);
    }

    private static ConfigPropDto ToConfigPropDto(IConfigService config, string prop)
    {
        var info = config.GetPropInfo(prop);
        return new(prop,
            config.GetSetting(prop),
            config.GetComment(prop),
            info is null ? ConfigPropTypes.TEXT : ConfigPropTypes.Of(info.Type),
            info?.Min,
            info?.Max,
            info?.MaxLength,
            info?.Options);
    }
}
