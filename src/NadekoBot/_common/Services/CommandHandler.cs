using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.Configs;
using NadekoBot.Db.Models;
using ExecuteResult = Discord.Commands.ExecuteResult;
using PreconditionResult = Discord.Commands.PreconditionResult;

namespace NadekoBot.Services;

public sealed partial class CommandHandler : INService, ICommandHandler
{
    private const float ONE_THOUSANDTH = 1.0f / 1000;

    public event Func<IUserMessage, CommandInfo, Task> CommandExecuted = static delegate { return Task.CompletedTask; };
    public event Func<CommandInfo, ITextChannel?, string, Task> CommandErrored = static delegate { return Task.CompletedTask; };

    private readonly DiscordSocketClient _client;
    private readonly CommandService _commandService;
    private readonly BotConfigService _bcs;
    private readonly IBehaviorHandler _behaviorHandler;
    private readonly IServiceProvider _services;
    private readonly ShardData _shardData;

    private ConcurrentDictionary<ulong, string> _prefixes = new();

    private readonly DbService _db;

    public CommandHandler(
        DiscordSocketClient client,
        DbService db,
        CommandService commandService,
        BotConfigService bcs,
        IBehaviorHandler behaviorHandler,
        IServiceProvider services,
        ShardData shardData)
    {
        _client = client;
        _commandService = commandService;
        _bcs = bcs;
        _behaviorHandler = behaviorHandler;
        _db = db;
        _services = services;
        _shardData = shardData;
    }

    public async Task InitializeAsync()
    {
        await using var uow = _db.GetDbContext();
        _prefixes = await uow.GetTable<GuildConfig>()
            .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
            .Where(x => x.Prefix != null)
            .ToListAsyncLinqToDB()
            .Pipe(x => x.ToDictionary(x => x.GuildId, x => x.Prefix!).ToConcurrent());
    }

    public string GetPrefix(IGuild? guild)
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
        return Task.CompletedTask;
    }

    private void LogSuccessfulExecution(IUserMessage usrMsg, ITextChannel? channel, int interceptMs, int totalMs)
    {
        var outputType = _bcs.Data.ConsoleOutputType;

        if (outputType == ConsoleOutputType.Normal)
        {
            Log.Information("""
                            Command Executed after {ExecTime}s
                            	User: {User}
                            	Server: {Server}
                            	Channel: {Channel}
                            	Message: {Message}
                            """,
                string.Create(null, stackalloc char[32],
                    $"{interceptMs * ONE_THOUSANDTH:F3}/{totalMs * ONE_THOUSANDTH:F3}"),
                usrMsg.Author + " [" + usrMsg.Author.Id + "]",
                channel is null ? "PRIVATE" : channel.Guild.Name + " [" + channel.Guild.Id + "]",
                channel is null ? "PRIVATE" : channel.Name + " [" + channel.Id + "]",
                usrMsg.Content);
        }
        else if (outputType == ConsoleOutputType.Simple)
        {
            Log.Information("Succ | g:{GuildId} | c: {ChannelId} | u: {UserId} | msg: {Message}",
                channel?.Guild.Id.ToString() ?? "-",
                channel?.Id.ToString() ?? "-",
                usrMsg.Author.Id.ToString(),
                usrMsg.Content.TrimTo(10));
        }
    }

    private void LogErroredExecution(
        string errorMessage,
        IUserMessage usrMsg,
        ITextChannel? channel,
        int interceptMs,
        int totalMs)
    {
        var outputType = _bcs.Data.ConsoleOutputType;

        if (outputType == ConsoleOutputType.Normal)
        {
            Log.Warning("""
                        Command Errored after {ExecTime}s
                        	User: {User}
                        	Server: {Guild}
                        	Channel: {Channel}
                        	Message: {Message}
                        	Error: {ErrorMessage}
                        """,
                string.Create(null, stackalloc char[32],
                    $"{interceptMs * ONE_THOUSANDTH:F3}/{totalMs * ONE_THOUSANDTH:F3}"),
                usrMsg.Author + " [" + usrMsg.Author.Id + "]",
                channel is null ? "DM" : channel.Guild.Name + " [" + channel.Guild.Id + "]",
                channel is null ? "DM" : channel.Name + " [" + channel.Id + "]",
                usrMsg.Content,
                errorMessage);
        }
        else if (outputType == ConsoleOutputType.Simple)
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
        if (_bcs.Data.IgnoreOtherBots)
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

    public async Task TryRunCommand(SocketGuild? guild, ISocketMessageChannel channel, IUserMessage usrMsg)
    {
        var startTime = Environment.TickCount;

        var intercepted = await _behaviorHandler.RunExecOnMessageAsync(guild, usrMsg);
        if (intercepted)
            return;

        var interceptTime = Environment.TickCount - startTime;

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
                    LogSuccessfulExecution(usrMsg, channel as ITextChannel, interceptTime, startTime);
                    await CommandExecuted(usrMsg, info);
                    await _behaviorHandler.RunPostCommandAsync(context, info.Module.GetTopLevelModule().Name, info);
                    return;
                }

                // if it errored
                if (error is not null)
                {
                    error = HumanizeError(error);
                    LogErroredExecution(error, usrMsg, channel as ITextChannel, interceptTime, startTime);

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
            || error.Contains("parse float", StringComparison.OrdinalIgnoreCase))
            return "Invalid number specified. Make sure you're specifying parameters in the correct order.";

        return error;
    }

}