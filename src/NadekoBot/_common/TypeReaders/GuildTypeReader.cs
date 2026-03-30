#nullable disable
namespace NadekoBot.Common.TypeReaders;

public sealed class GuildTypeReader : NadekoTypeReader<IGuild>
{
    private readonly DiscordSocketClient _client;

    public GuildTypeReader(DiscordSocketClient client)
        => _client = client;

    public override ValueTask<TypeReaderResult<IGuild>> ReadAsync(ICommandContext context, string input)
    {
        input = input.Trim();
        var guilds = _client.Guilds;
        IGuild guild = guilds.FirstOrDefault(g =>
                           g.Id.ToString().Equals(input, StringComparison.InvariantCultureIgnoreCase))
                       ?? guilds.FirstOrDefault(g =>
                           g.Name.Trim().Equals(input, StringComparison.InvariantCultureIgnoreCase));

        if (guild is not null)
            return new(TypeReaderResult.FromSuccess(guild));

        return new(
            TypeReaderResult.FromError<IGuild>(CommandError.ParseFailed, "No guild by that name or Id found"));
    }
}