namespace NadekoBot.Common;

public sealed class ShardData : INService
{
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;

    public int TotalShards
        => _creds.TotalShards;

    public int ShardId
        => _client.ShardId;

    public ShardData(DiscordSocketClient client, IBotCreds creds)
    {
        _client = client;
        _creds = creds;
    }
}