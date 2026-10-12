using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace NadekoBot.Hosted;

// The guild of a /guilds/{guildId} route. ApiGateMiddleware resolves it once and answers 404 for an unknown guild.
public readonly record struct GuildRef(SocketGuild Guild)
{
    private static readonly object _itemKey = new();

    public ulong Id
        => Guild.Id;

    public static void Set(HttpContext http, SocketGuild guild)
        => http.Items[_itemKey] = guild;

    public static bool TryGet(HttpContext http, [NotNullWhen(true)] out SocketGuild? guild)
    {
        guild = http.Items.TryGetValue(_itemKey, out var item) ? item as SocketGuild : null;
        return guild is not null;
    }

    public static ValueTask<GuildRef?> BindAsync(HttpContext http)
        => ValueTask.FromResult<GuildRef?>(TryGet(http, out var guild) ? new GuildRef(guild) : null);
}
