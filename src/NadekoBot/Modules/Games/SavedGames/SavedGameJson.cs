using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NadekoBot.Modules.Gambling.Common.AnimalRacing;
using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Games.Hangman;

namespace NadekoBot.Modules.Games.SavedGames;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HangmanSave))]
[JsonSerializable(typeof(TriviaSave))]
[JsonSerializable(typeof(RaceSave))]
public sealed partial class SavedGameJsonContext : JsonSerializerContext
{
}

public static class SavedGameJson
{
    public static byte[] Write<T>(T value, JsonTypeInfo<T> info)
        => JsonSerializer.SerializeToUtf8Bytes(value, info);

    // null when the bytes hold no state of this type; the restore then drops the game
    public static T? Read<T>(byte[] data, JsonTypeInfo<T> info)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(data, info);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Could not read a saved {Type}", typeof(T).Name);
            return null;
        }
    }
}

// The channel of a saved game, when the bot can still post the game there.
public static class SavedGameChannel
{
    // retry is true when the server is in a Discord outage, because then it can come back
    public static SocketTextChannel? Find(DiscordSocketClient client, SavedGame game, out bool retry)
    {
        retry = false;
        if (client.GetGuild(game.GuildId) is not { } guild)
            return null;

        if (!((IGuild)guild).Available || guild.CurrentUser is not { } bot)
        {
            retry = true;
            return null;
        }

        if (guild.GetTextChannel(game.ChannelId) is not { } channel)
            return null;

        var perms = bot.GetPermissions(channel);
        return perms is { ViewChannel: true, SendMessages: true, EmbedLinks: true } ? channel : null;
    }
}
