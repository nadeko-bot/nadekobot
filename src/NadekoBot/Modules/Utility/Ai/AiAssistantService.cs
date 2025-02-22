using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Games.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NadekoBot.Modules.Utility;

public enum GetCommandErrorResult
{
    RateLimitHit,
    NotAuthorized,
    Disregard,
    Unknown
}

public sealed class AiAssistantService
    : IAiAssistantService, IReadyExecutor,
        IExecOnMessage,
        INService
{
    private IReadOnlyCollection<AiCommandModel> _commands = [];

    private readonly IBotStrings _strings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly CommandService _cmds;
    private readonly IBotCredsProvider _credsProvider;
    private readonly DiscordSocketClient _client;
    private readonly ICommandHandler _cmdHandler;
    private readonly BotConfigService _bcs;
    private readonly IMessageSenderService _sender;

    private readonly JsonSerializerOptions _serializerOptions = new();
    private readonly IPermissionChecker _permChecker;
    private readonly IBotCache _botCache;
    private readonly ChatterBotService _cbs;

    public AiAssistantService(
        DiscordSocketClient client,
        IBotStrings strings,
        IHttpClientFactory httpFactory,
        CommandService cmds,
        IBotCredsProvider credsProvider,
        ICommandHandler cmdHandler,
        BotConfigService bcs,
        IPermissionChecker permChecker,
        IBotCache botCache,
        ChatterBotService cbs,
        IMessageSenderService sender)
    {
        _client = client;
        _strings = strings;
        _httpFactory = httpFactory;
        _cmds = cmds;
        _credsProvider = credsProvider;
        _cmdHandler = cmdHandler;
        _bcs = bcs;
        _sender = sender;
        _permChecker = permChecker;
        _botCache = botCache;
        _cbs = cbs;
    }

    public async Task<OneOf.OneOf<NadekoCommandCallModel, GetCommandErrorResult>> TryGetCommandAsync(
        ulong userId,
        string prompt,
        IReadOnlyCollection<AiCommandModel> commands,
        string prefix)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                query = prompt,
                commands = commands.ToDictionary(x => x.Name,
                    x => new AiCommandModel()
                    {
                        Desc = string.Format(x.Desc ?? "", prefix),
                        Params = x.Params,
                        Name = x.Name
                    }),
            }),
            Encoding.UTF8,
            "application/json"
        );

        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Post;
        // request.RequestUri = new("https://nai.nadeko.bot/get-command");
        request.RequestUri = new("https://nai.nadeko.bot/get-command");
        request.Content = content;

        var creds = _credsProvider.GetCreds();

        request.Headers.TryAddWithoutValidation("x-auth-token", creds.NadekoAiToken);
        request.Headers.TryAddWithoutValidation("x-auth-userid", userId.ToString());


        using var client = _httpFactory.CreateClient();

        using var response = await client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return GetCommandErrorResult.RateLimitHit;
        }
        else if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return GetCommandErrorResult.NotAuthorized;
        }

        var funcModel = await response.Content.ReadFromJsonAsync<CommandPromptResultModel>();


        if (funcModel?.Name == "disregard")
        {
            Log.Warning("Disregarding the prompt: {Prompt}", prompt);
            return GetCommandErrorResult.Disregard;
        }

        if (funcModel is null)
            return GetCommandErrorResult.Unknown;

        var comModel = new NadekoCommandCallModel()
        {
            Name = funcModel.Name,
            Arguments = funcModel.Arguments
                                 .OrderBy(param => _commands.FirstOrDefault(x => x.Name == funcModel.Name)
                                                            ?.Params
                                                            .Select((x, i) => (x, i))
                                                            .Where(x => x.x.Name == param.Key)
                                                            .Select(x => x.i)
                                                            .FirstOrDefault())
                                 .Select(x => x.Value)
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .ToArray(),
            Remaining = funcModel.Remaining
        };

        return comModel;
    }

    public IReadOnlyCollection<AiCommandModel> GetCommands()
        => _commands;

    public Task OnReadyAsync()
    {
        var cmds = _cmds.Commands
                        .Select(x => (MethodName: x.Summary, CommandName: x.Aliases[0]))
                        .Where(x => !x.MethodName.Contains("///"))
                        .Distinct()
                        .ToList();

        var funcs = new List<AiCommandModel>();
        foreach (var (method, cmd) in cmds)
        {
            var commandStrings = _strings.GetCommandStrings(method);

            if (commandStrings is null)
                continue;

            funcs.Add(new()
            {
                Name = cmd,
                Desc = commandStrings.Desc?.Replace("currency", "flowers") ?? string.Empty,
                Params = commandStrings.Params.FirstOrDefault()
                                       ?.Select(x => new AiCommandParamModel()
                                       {
                                           Desc = x.Value.Desc,
                                           Name = x.Key,
                                       })
                                       .ToArray()
                         ?? []
            });
        }

        _commands = funcs;

        return Task.CompletedTask;
    }

    public int Priority
        => 2;

    public async Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        if (string.IsNullOrWhiteSpace(_credsProvider.GetCreds().NadekoAiToken))
            return false;

        if (guild is not SocketGuild sg)
            return false;

        var nadekoId = _client.CurrentUser.Id;

        var channel = msg.Channel as ITextChannel;
        if (channel is null)
            return false;

        var normalMention = $"<@{nadekoId}> ";
        var nickMention = $"<@!{nadekoId}> ";
        string query;
        if (msg.Content.StartsWith(normalMention, StringComparison.InvariantCulture))
            query = msg.Content[normalMention.Length..].Trim();
        else if (msg.Content.StartsWith(nickMention, StringComparison.InvariantCulture))
            query = msg.Content[nickMention.Length..].Trim();
        else
            return false;

        var success = await TryExecuteAiCommand(guild, msg, channel, query);

        return success;
    }

    public async Task<bool> TryExecuteAiCommand(
        IGuild guild,
        IUserMessage msg,
        ITextChannel channel,
        string query)
    {
        if (guild is not SocketGuild sg)
            return false;

        // check permissions
        var pcResult = await _permChecker.CheckPermsAsync(
            guild,
            msg.Channel,
            msg.Author,
            "Utility",
            "prompt"
        );

        if (!pcResult.IsAllowed)
            return false;

        using var _ = channel.EnterTypingState();

        var result = await TryGetCommandAsync(msg.Author.Id, query, _commands, _cmdHandler.GetPrefix(guild.Id));

        if (result.TryPickT0(out var model, out var error))
        {
            if (model.Name == ".ai_chat")
            {
                var sess = _cbs.GetOrCreateSession(guild.Id);
                if (sess is null)
                    return false;

                await _cbs.RunChatterBot(sg, msg, channel, sess, query);
                return true;
            }

            var commandString = GetCommandString(model);

            var msgTask = _sender.Response(channel)
                                 .Embed(_sender.CreateEmbed(guild?.Id)
                                               .WithOkColor()
                                               .WithAuthor(msg.Author.GlobalName,
                                                   msg.Author.RealAvatarUrl().ToString())
                                               .WithDescription(commandString))
                                 .SendAsync();


            await _cmdHandler.TryRunCommand(sg,
                (ISocketMessageChannel)channel,
                new DoAsUserMessage((SocketUserMessage)msg, msg.Author, commandString));

            var cmdMsg = await msgTask;

            cmdMsg.DeleteAfter(5);

            return true;
        }

        if (error == GetCommandErrorResult.Disregard)
        {
            // await msg.ErrorAsync();
            return false;
        }

        var key = new TypedKey<bool>($"sub_error:{msg.Author.Id}:{error}");

        if (!await _botCache.AddAsync(key, true, TimeSpan.FromDays(1), overwrite: false))
            return false;

        var errorMsg = error switch
        {
            GetCommandErrorResult.RateLimitHit
                => "You've spent your daily requests quota.",
            GetCommandErrorResult.NotAuthorized
                => "In order to use this command you have to have a 5$ or higher subscription at <https://patreon.com/nadekobot>",
            GetCommandErrorResult.Unknown
                => "The service is temporarily unavailable.",
            _ => throw new ArgumentOutOfRangeException()
        };

        await _sender.Response(channel)
                     .Error(errorMsg)
                     .SendAsync();

        return true;
    }

    private string GetCommandString(NadekoCommandCallModel res)
        => $"{_bcs.Data.Prefix}{res.Name} {res.Arguments.Select((x, i) => GetParamString(x, i + 1 == res.Arguments.Count)).Join(" ")}";

    private static string GetParamString(string val, bool isLast)
        => isLast ? val : "\"" + val + "\"";
}