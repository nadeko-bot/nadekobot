#nullable disable
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.Configs;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using ExecuteResult = Discord.Commands.ExecuteResult;
using PreconditionResult = Discord.Commands.PreconditionResult;

namespace NadekoBot.Services;

public class CommandHandler : INService, IReadyExecutor, ICommandHandler
{
    private const int GLOBAL_COMMANDS_COOLDOWN = 200;

    private const float ONE_THOUSANDTH = 1.0f / 1000;

    public event Func<IUserMessage, CommandInfo, Task> CommandExecuted = delegate { return Task.CompletedTask; };
    public event Func<CommandInfo, ITextChannel, string, Task> CommandErrored = delegate { return Task.CompletedTask; };

    //userid/msg count
    public ConcurrentDictionary<ulong, uint> UserMessagesSent { get; } = new();

    public ConcurrentHashSet<ulong> UsersOnShortCooldown { get; } = new();

    private readonly DiscordSocketClient _client;
    private readonly CommandService _commandService;
    private readonly BotConfigService _bcs;
    private readonly IBot _bot;
    private readonly IBehaviorHandler _behaviorHandler;
    private readonly IServiceProvider _services;
    private readonly ShardData _shardData;

    private ConcurrentDictionary<ulong, string> _prefixes;

    private readonly DbService _db;

    private readonly BotConfig _bc;

    public CommandHandler(
        DiscordSocketClient client,
        DbService db,
        CommandService commandService,
        BotConfigService bcs,
        IBot bot,
        IBehaviorHandler behaviorHandler,
        IServiceProvider services,
        ShardData shardData)
    {
        _client = client;
        _commandService = commandService;
        _bc = bcs.Data;
        _bcs = bcs;
        _bot = bot;
        _behaviorHandler = behaviorHandler;
        _db = db;
        _services = services;
        _shardData = shardData;
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            _prefixes = await uow.GetTable<GuildConfig>()
                                 .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                                 .Where(x => x.Prefix != null)
                                 .ToListAsyncLinqToDB()
                                 .Fmap(x => x.ToDictionary(x => x.GuildId, x => x.Prefix).ToConcurrent());
        }

        // clear users on short cooldown every GLOBAL_COMMANDS_COOLDOWN miliseconds
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(GLOBAL_COMMANDS_COOLDOWN));
        while (await timer.WaitForNextTickAsync())
            UsersOnShortCooldown.Clear();
    }

    public string GetPrefix(IGuild guild)
        => GetPrefix(guild?.Id);

    public string GetPrefix(ulong? id = null)
    {
        if (id is null || !_prefixes.TryGetValue(id.Value, out var prefix))
            return _bcs.Data.Prefix;

        return prefix;
    }

    public string SetDefaultPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentNullException(nameof(prefix));

        _bcs.ModifyConfig(bs =>
        {
            bs.Prefix = prefix;
        });

        return prefix;
    }

    public string SetPrefix(IGuild guild, string prefix)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(guild);

        using (var uow = _db.GetDbContext())
        {
            var gc = uow.GuildConfigsForId(guild.Id, set => set);
            gc.Prefix = prefix;
            uow.SaveChanges();
        }

        _prefixes[guild.Id] = prefix;

        return prefix;
    }

    public async Task ExecuteExternal(ulong? guildId, ulong channelId, string commandText)
    {
        if (guildId is not null)
        {
            var guild = _client.GetGuild(guildId.Value);
            if (guild?.GetChannel(channelId) is not SocketTextChannel channel)
            {
                Log.Warning("Channel for external execution not found");
                return;
            }

            try
            {
                IUserMessage msg = await channel.SendMessageAsync(commandText);
                msg = (IUserMessage)await channel.GetMessageAsync(msg.Id);
                await TryRunCommand(guild, channel, msg);
                //msg.DeleteAfter(5);
            }
            catch { }
        }
    }

    public Task StartHandling()
    {
        _client.MessageReceived += MessageReceivedHandler;
        // _client.SlashCommandExecuted += SlashCommandExecuted;
        return Task.CompletedTask;
    }

    // private async Task SlashCommandExecuted(SocketSlashCommand arg)
    // {
    //     var ctx = new SocketInteractionContext<SocketSlashCommand>(_client, arg);
    //     await _interactions.ExecuteCommandAsync(ctx, _services);
    // }

    private Task LogSuccessfulExecution(IUserMessage usrMsg, ITextChannel channel, params int[] execPoints)
    {
        if (_bcs.Data.ConsoleOutputType == ConsoleOutputType.Normal)
        {
            Log.Information("""
                            Command Executed after {ExecTime}s
                            	User: {User}
                            	Server: {Server}
                            	Channel: {Channel}
                            	Message: {Message}
                            """,
                string.Join("/", execPoints.Select(x => (x * ONE_THOUSANDTH).ToString("F3"))),
                usrMsg.Author + " [" + usrMsg.Author.Id + "]",
                channel is null ? "PRIVATE" : channel.Guild.Name + " [" + channel.Guild.Id + "]",
                channel is null ? "PRIVATE" : channel.Name + " [" + channel.Id + "]",
                usrMsg.Content);
        }
        else
        {
            Log.Information("Succ | g:{GuildId} | c: {ChannelId} | u: {UserId} | msg: {Message}",
                channel?.Guild.Id.ToString() ?? "-",
                channel?.Id.ToString() ?? "-",
                usrMsg.Author.Id.ToString(),
                usrMsg.Content.TrimTo(10));
        }

        return Task.CompletedTask;
    }

    private void LogErroredExecution(
        string errorMessage,
        IUserMessage usrMsg,
        ITextChannel channel,
        params int[] execPoints)
    {
        if (_bcs.Data.ConsoleOutputType == ConsoleOutputType.Normal)
        {
            Log.Warning("""
                        Command Errored after {ExecTime}s
                        	User: {User}
                        	Server: {Guild}
                        	Channel: {Channel}
                        	Message: {Message}
                        	Error: {ErrorMessage}
                        """,
                string.Join("/", execPoints.Select(x => (x * ONE_THOUSANDTH).ToString("F3"))),
                usrMsg.Author + " [" + usrMsg.Author.Id + "]",
                channel is null ? "DM" : channel.Guild.Name + " [" + channel.Guild.Id + "]",
                channel is null ? "DM" : channel.Name + " [" + channel.Id + "]",
                usrMsg.Content,
                errorMessage);
        }
        else
        {
            Log.Warning("""
                        Err | g:{GuildId} | c: {ChannelId} | u: {UserId} | msg: {Message}
                        	Err: {ErrorMessage}
                        """,
                channel?.Guild.Id.ToString() ?? "-",
                channel?.Id.ToString() ?? "-",
                usrMsg.Author.Id,
                usrMsg.Content.TrimTo(10),
                errorMessage);
        }
    }

    private Task MessageReceivedHandler(SocketMessage msg)
    {
        if (!_bot.IsReady)
            return Task.CompletedTask;

        if (_bc.IgnoreOtherBots)
        {
            if (msg.Author.IsBot)
                return Task.CompletedTask;
        }
        else if (msg.Author.Id == _client.CurrentUser.Id)
            return Task.CompletedTask;

        if (msg is not SocketUserMessage usrMsg)
            return Task.CompletedTask;

        Task.Run(async () =>
        {
            try
            {
#if !GLOBAL_NADEKO
                // track how many messages each user is sending
                UserMessagesSent.AddOrUpdate(usrMsg.Author.Id, 1, (_, old) => ++old);
#endif

                var channel = msg.Channel;
                var guild = (msg.Channel as SocketTextChannel)?.Guild;

                await TryRunCommand(guild, channel, usrMsg);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in CommandHandler");
                if (ex.InnerException is not null)
                    Log.Warning(ex.InnerException, "Inner Exception of the error in CommandHandler");
            }
        });

        return Task.CompletedTask;
    }

    public async Task TryRunCommand(SocketGuild guild, ISocketMessageChannel channel, IUserMessage usrMsg)
    {
        var startTime = Environment.TickCount;

        var blocked = await _behaviorHandler.RunExecOnMessageAsync(guild, usrMsg);
        if (blocked)
            return;

        var blockTime = Environment.TickCount - startTime;

        var messageContent = await _behaviorHandler.RunInputTransformersAsync(guild, usrMsg);

        var prefix = GetPrefix(guild?.Id);
        var isPrefixCommand = messageContent.StartsWith(".prefix", StringComparison.InvariantCultureIgnoreCase);
        // execute the command and measure the time it took
        if (isPrefixCommand || messageContent.StartsWith(prefix, StringComparison.InvariantCulture))
        {
            var context = new CommandContext(_client, usrMsg);
            var (success, error, info) = await ExecuteCommandAsync(context,
                messageContent,
                isPrefixCommand ? 1 : prefix.Length,
                _services,
                MultiMatchHandling.Best);

            startTime = Environment.TickCount - startTime;

            // if a command is found
            if (info is not null)
            {
                // if it successfully executed
                if (success)
                {
                    await LogSuccessfulExecution(usrMsg, channel as ITextChannel, blockTime, startTime);
                    await CommandExecuted(usrMsg, info);
                    await _behaviorHandler.RunPostCommandAsync(context, info.Module.GetTopLevelModule().Name, info);
                    return;
                }

                // if it errored
                if (error is not null)
                {
                    error = HumanizeError(error);
                    LogErroredExecution(error, usrMsg, channel as ITextChannel, blockTime, startTime);

                    if (guild is not null)
                        await CommandErrored(info, channel as ITextChannel, error);

                    return;
                }
            }
        }

        await _behaviorHandler.RunOnNoCommandAsync(guild, usrMsg);
    }

    private string HumanizeError(string error)
    {
        if (error.Contains("parse int", StringComparison.OrdinalIgnoreCase)
            || error.Contains("parse float"))
            return "Invalid number specified. Make sure you're specifying parameters in the correct order.";

        return error;
    }

    public Task<(bool Success, string Error, CommandInfo Info)> ExecuteCommandAsync(
        ICommandContext context,
        string input,
        int argPos,
        IServiceProvider serviceProvider,
        MultiMatchHandling multiMatchHandling = MultiMatchHandling.Exception)
        => ExecuteCommand(context, input[argPos..], serviceProvider, multiMatchHandling);


    public async Task<(bool Success, string Error, CommandInfo Info)> ExecuteCommand(
        ICommandContext context,
        string input,
        IServiceProvider services,
        MultiMatchHandling multiMatchHandling = MultiMatchHandling.Exception)
    {
        var searchResult = _commandService.Search(context, input);
        if (!searchResult.IsSuccess)
            return (false, null, null);

        var commands = searchResult.Commands;
        var preconditionResults = new Dictionary<CommandMatch, PreconditionResult>();

        foreach (var match in commands)
            preconditionResults[match] = await match.Command.CheckPreconditionsAsync(context, services);

        var successfulPreconditions = preconditionResults.Where(x => x.Value.IsSuccess).ToArray();

        if (successfulPreconditions.Length == 0)
        {
            //All preconditions failed, return the one from the highest priority command
            var bestCandidate = preconditionResults.OrderByDescending(x => x.Key.Command.Priority)
                                                   .FirstOrDefault(x => !x.Value.IsSuccess);
            return (false, bestCandidate.Value.ErrorReason, commands[0].Command);
        }

        var parseResultsDict = new Dictionary<CommandMatch, ParseResult>();
        foreach (var pair in successfulPreconditions)
        {
            var parseResult = await pair.Key.ParseAsync(context, searchResult, pair.Value, services);

            if (parseResult.Error == CommandError.MultipleMatches)
            {
                IReadOnlyList<TypeReaderValue> argList, paramList;
                switch (multiMatchHandling)
                {
                    case MultiMatchHandling.Best:
                        argList = parseResult.ArgValues
                                             .Map(x => x.Values.MaxBy(y => y.Score));
                        paramList = parseResult.ParamValues
                                               .Map(x => x.Values.MaxBy(y => y.Score));
                        parseResult = ParseResult.FromSuccess(argList, paramList);
                        break;
                }
            }

            parseResultsDict[pair.Key] = parseResult;
        }

        // Calculates the 'score' of a command given a parse result
        float CalculateScore(CommandMatch match, ParseResult parseResult)
        {
            float argValuesScore = 0, paramValuesScore = 0;

            if (match.Command.Parameters.Count > 0)
            {
                var argValuesSum =
                    parseResult.ArgValues?.Sum(x => x.Values.OrderByDescending(y => y.Score).FirstOrDefault().Score)
                    ?? 0;
                var paramValuesSum =
                    parseResult.ParamValues?.Sum(x => x.Values.OrderByDescending(y => y.Score).FirstOrDefault().Score)
                    ?? 0;

                argValuesScore = argValuesSum / match.Command.Parameters.Count;
                paramValuesScore = paramValuesSum / match.Command.Parameters.Count;
            }

            var totalArgsScore = (argValuesScore + paramValuesScore) / 2;
            return match.Command.Priority + (totalArgsScore * 0.99f);
        }

        //Order the parse results by their score so that we choose the most likely result to execute
        var parseResults = parseResultsDict.OrderByDescending(x => CalculateScore(x.Key, x.Value)).ToList();

        var successfulParses = parseResults.Where(x => x.Value.IsSuccess).ToArray();

        if (successfulParses.Length == 0)
        {
            //All parses failed, return the one from the highest priority command, using score as a tie breaker
            var bestMatch = parseResults.FirstOrDefault(x => !x.Value.IsSuccess);
            return (false, bestMatch.Value.ErrorReason, commands[0].Command);
        }

        var cmd = successfulParses[0].Key.Command;

        // Bot will ignore commands which are ran more often than what specified by
        // GlobalCommandsCooldown constant (miliseconds)
        if (!UsersOnShortCooldown.Add(context.Message.Author.Id))
            return (false, null, cmd);
        //return SearchResult.FromError(CommandError.Exception, "You are on a global cooldown.");

        var blocked = await _behaviorHandler.RunPreCommandAsync(context, cmd);
        if (blocked)
            return (false, null, cmd);

        //If we get this far, at least one parse was successful. Execute the most likely overload.
        var chosenOverload = successfulParses[0];
        var execResult = (ExecuteResult)await chosenOverload.Key.ExecuteAsync(context, chosenOverload.Value, services);

        if (execResult.Exception is not null
            && (execResult.Exception is not HttpException he
                || he.DiscordCode != DiscordErrorCode.InsufficientPermissions))
            Log.Warning(execResult.Exception, "Command Error");

        return (true, null, cmd);
    }
}