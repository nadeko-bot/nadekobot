#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.Common.ChatterBot;
using NadekoBot.Modules.Patronage;
using NadekoBot.Modules.Permissions;

namespace NadekoBot.Modules.Games.Services;

public class ChatterBotService : IExecOnMessage, IReadyExecutor
{
    private ConcurrentDictionary<ulong, Lazy<IChatterBotSession>> _chatterBotGuilds;

    public int Priority
        => 1;

    private readonly DiscordSocketClient _client;
    private readonly IPermissionChecker _perms;
    private readonly IBotCreds _creds;
    private readonly IHttpClientFactory _httpFactory;
    private readonly GamesConfigService _gcs;
    private readonly IMessageSenderService _sender;
    private readonly DbService _db;
    public readonly IPatronageService _ps;

    public ChatterBotService(
        DiscordSocketClient client,
        IPermissionChecker perms,
        IPatronageService ps,
        IHttpClientFactory factory,
        IBotCreds creds,
        GamesConfigService gcs,
        IMessageSenderService sender,
        DbService db)
    {
        _client = client;
        _perms = perms;
        _creds = creds;
        _sender = sender;
        _db = db;
        _httpFactory = factory;
        _perms = perms;
        _gcs = gcs;
        _ps = ps;
    }

    public IChatterBotSession CreateSession()
    {
        switch (_gcs.Data.ChatBot)
        {
            case ChatBotImplementation.Cleverbot:
                if (!string.IsNullOrWhiteSpace(_creds.CleverbotApiKey))
                    return new OfficialCleverbotSession(_creds.CleverbotApiKey, _httpFactory);

                Log.Information("Cleverbot will not work as the api key is missing");
                return null;
            case ChatBotImplementation.OpenAi:
                var data = _gcs.Data;
                if (!string.IsNullOrWhiteSpace(_creds.Gpt3ApiKey))
                    return new OpenAiApiSession(
                        data.ChatGpt.ApiUrl,
                        _creds.Gpt3ApiKey,
                        data.ChatGpt.ModelName,
                        data.ChatGpt.ChatHistory,
                        data.ChatGpt.MaxTokens,
                        data.ChatGpt.MinTokens,
                        data.ChatGpt.PersonalityPrompt,
                        _client.CurrentUser.Username,
                        _httpFactory);

                Log.Information("Openai Api will likely not work as the api key is missing");
                return null;
            default:
                return null;
        }
    }

    public IChatterBotSession GetOrCreateSession(ulong guildId)
    {
        if (_chatterBotGuilds.TryGetValue(guildId, out var lazyChatBot))
            return lazyChatBot.Value;

        lazyChatBot = new(() => CreateSession(), true);
        _chatterBotGuilds.TryAdd(guildId, lazyChatBot);
        return lazyChatBot.Value;
    }

    public string PrepareMessage(IUserMessage msg)
    {
        var nadekoId = _client.CurrentUser.Id;
        var normalMention = $"<@{nadekoId}> ";
        var nickMention = $"<@!{nadekoId}> ";
        string message;
        if (msg.Content.StartsWith(normalMention, StringComparison.InvariantCulture))
            message = msg.Content[normalMention.Length..].Trim();
        else if (msg.Content.StartsWith(nickMention, StringComparison.InvariantCulture))
            message = msg.Content[nickMention.Length..].Trim();
        else if (msg.ReferencedMessage?.Author.Id == nadekoId)
            message = msg.Content;
        else
            return null;

        return message;
    }

    public async Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is not SocketGuild sg)
            return false;

        var channel = usrMsg.Channel as ITextChannel;
        if (channel is null)
            return false;

        if (!_chatterBotGuilds.TryGetValue(channel.Guild.Id, out var lazyChatBot))
            return false;

        var chatBot = lazyChatBot.Value;
        var message = PrepareMessage(usrMsg);
        if (message is null)
            return false;

        return await RunChatterBot(sg, usrMsg, channel, chatBot, message);
    }

    public async Task<bool> RunChatterBot(
        SocketGuild guild,
        IUserMessage usrMsg,
        ITextChannel channel,
        IChatterBotSession chatBot,
        string message)
    {
        try
        {
            var res = await _perms.CheckPermsAsync(guild,
                usrMsg.Channel,
                usrMsg.Author,
                CleverBotResponseStr.CLEVERBOT_RESPONSE,
                CleverBotResponseStr.CLEVERBOT_RESPONSE);

            if (!res.IsAllowed)
                return false;

            if (!await _ps.LimitHitAsync(LimitedFeatureName.ChatBot, usrMsg.Author.Id, 2048 / 2))
            {
                // limit exceeded
                return false;
            }

            _ = channel.TriggerTypingAsync();
            var response = await chatBot.Think(message, usrMsg.Author.ToString());

            if (response.TryPickT0(out var result, out var error))
            {
                // calculate the diff in case we overestimated user's usage
                var inTokens = (result.TokensIn - 2048) / 2;

                // add the output tokens to the limit
                await _ps.LimitForceHit(LimitedFeatureName.ChatBot,
                    usrMsg.Author.Id,
                    (inTokens) + (result.TokensOut / 2 * 3));

                await _sender.Response(channel)
                             .Confirm(result.Text)
                             .SendAsync();
            }
            else
            {
                Log.Warning("Error in chatterbot: {Error}", error.Value);
            }

            Log.Information("""
                            CleverBot Executed
                            Server: {GuildName} [{GuildId}]
                            Channel: {ChannelName} [{ChannelId}]
                            UserId: {Author} [{AuthorId}]
                            Message: {Content}
                            """,
                guild.Name,
                guild.Id,
                usrMsg.Channel?.Name,
                usrMsg.Channel?.Id,
                usrMsg.Author,
                usrMsg.Author.Id,
                usrMsg.Content);

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in cleverbot");
        }

        return false;
    }

    public async Task<bool> ToggleChatterBotAsync(ulong guildId)
    {
        if (_chatterBotGuilds.TryRemove(guildId, out _))
        {
            await using var uow = _db.GetDbContext();
            await uow.Set<GuildConfig>()
                     .ToLinqToDBTable()
                     .Where(x => x.GuildId == guildId)
                     .UpdateAsync((gc) => new GuildConfig()
                     {
                         CleverbotEnabled = false
                     });
            await uow.SaveChangesAsync();
            return false;
        }

        _chatterBotGuilds.TryAdd(guildId, new(() => CreateSession(), true));

        await using (var uow = _db.GetDbContext())
        {
            await uow.Set<GuildConfig>()
                     .ToLinqToDBTable()
                     .Where(x => x.GuildId == guildId)
                     .UpdateAsync((gc) => new GuildConfig()
                     {
                         CleverbotEnabled = true
                     });

            await uow.SaveChangesAsync();
        }

        return true;
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        _chatterBotGuilds = uow.GuildConfigs
                               .AsNoTracking()
                               .Where(gc => gc.CleverbotEnabled)
                               .AsEnumerable()
                               .ToDictionary(gc => gc.GuildId,
                                   _ => new Lazy<IChatterBotSession>(() => CreateSession(), true))
                               .ToConcurrent();
    }
}