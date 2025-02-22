namespace NadekoBot.Extensions;

public sealed class MessageSenderService : IMessageSenderService, INService
{
    private readonly IBotStrings _bs;
    private readonly BotConfigService _bcs;
    private readonly DiscordSocketClient _client;
    private readonly IGuildColorsService _gcs;

    public MessageSenderService(
        IBotStrings bs,
        DiscordSocketClient client,
        IGuildColorsService gcs,
        BotConfigService bcs)
    {
        _bs = bs;
        _client = client;
        _gcs = gcs;
        _bcs = bcs;
    }


    public ResponseBuilder Response(IMessageChannel channel)
        => new ResponseBuilder(_bs, this, _client)
            .Channel(channel);

    public ResponseBuilder Response(ICommandContext ctx)
        => new ResponseBuilder(_bs, this, _client)
            .Context(ctx);

    public ResponseBuilder Response(IUser user)
        => new ResponseBuilder(_bs, this, _client)
            .User(user);

    public ResponseBuilder Response(SocketMessageComponent smc)
        => new ResponseBuilder(_bs, this, _client)
            .Channel(smc.Channel);

    public NadekoEmbedBuilder CreateEmbed(ulong? guildId = null)
        => new NadekoEmbedBuilder(_bcs, guildId is { } gid ? _gcs.GetColors(gid) : null);
}

public class NadekoEmbedBuilder : EmbedBuilder
{
    private readonly Color _okColor;
    private readonly Color _errorColor;
    private readonly Color _pendingColor;

    public NadekoEmbedBuilder(BotConfigService bcsData, Colors? guildColors = null)
    {
        var bcColors = bcsData.Data.Color;
        _okColor = guildColors?.Ok ?? bcColors.Ok.ToDiscordColor();
        _errorColor = guildColors?.Error ?? bcColors.Error.ToDiscordColor();
        _pendingColor = guildColors?.Warn ?? bcColors.Pending.ToDiscordColor();
    }

    public EmbedBuilder WithOkColor()
        => WithColor(_okColor);

    public EmbedBuilder WithErrorColor()
        => WithColor(_errorColor);

    public EmbedBuilder WithPendingColor()
        => WithColor(_pendingColor);
}