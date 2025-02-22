#nullable disable
using System.Collections;

namespace NadekoBot.Services;

public class StartingGuildsService : IEnumerable<ulong>, INService
{
    private readonly IReadOnlyList<ulong> _guilds;

    public StartingGuildsService(DiscordSocketClient client)
        => _guilds = client.Guilds.Select(x => x.Id).ToList();

    public IEnumerator<ulong> GetEnumerator()
        => _guilds.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _guilds.GetEnumerator();
}