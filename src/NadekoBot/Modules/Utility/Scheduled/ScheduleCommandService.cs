using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Utility.Scheduled;

public sealed class ScheduleCommandService(
    DbService db,
    ICommandHandler cmdHandler,
    DiscordSocketClient client,
    ShardData shardData,
    UtilityConfigService ucs) : INService, IReadyExecutor
{
    // Task.Delay throws for waits over ~49.7 days, and a long wait also keeps a stale plan
    private static readonly TimeSpan _maxWait = TimeSpan.FromDays(1);
    private static readonly TimeSpan _errorBackoff = TimeSpan.FromSeconds(10);

    private volatile TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int MaxScheduledPerUser
        => ucs.Data.MaxScheduledPerUser;

    public Task OnReadyAsync()
    {
        _ = Task.Run(RunLoopInternalAsync);
        return Task.CompletedTask;
    }

    private async Task RunLoopInternalAsync()
    {
        while (true)
        {
            // a due command whose guild is not cached yet would be deleted without running
            if (client.ConnectionState != ConnectionState.Connected)
            {
                await Task.Delay(_errorBackoff);
                continue;
            }

            try
            {
                await ProcessNextAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in ScheduleCommandService");
                await Task.Delay(_errorBackoff);
            }
        }
    }

    // runs the next due command, or waits until the next one is due or the list changes
    public async Task ProcessNextAsync()
    {
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ScheduledCommand? scheduledCommand;
        await using (var ctx = db.GetDbContext())
        {
            scheduledCommand = await ctx
                .GetTable<ScheduledCommand>()
                .Where(Queries.GuildOnShard<ScheduledCommand>(x => x.GuildId,
                    shardData.TotalShards,
                    shardData.ShardId))
                .OrderBy(x => x.When)
                .FirstOrDefaultAsyncLinqToDB();
        }

        if (scheduledCommand is null)
        {
            await _tcs.Task;
            return;
        }

        var diff = scheduledCommand.When - DateTime.UtcNow;
        if (diff > TimeSpan.Zero)
        {
            await Task.WhenAny(Task.Delay(diff < _maxWait ? diff : _maxWait), _tcs.Task);
            return;
        }

        int deleted;
        await using (var ctx = db.GetDbContext())
        {
            deleted = await ctx.GetTable<ScheduledCommand>()
                .Where(x => x.Id == scheduledCommand.Id)
                .DeleteAsync();
        }

        // the owner may have deleted it in the meantime
        if (deleted == 0)
            return;

        var guild = client.GetGuild(scheduledCommand.GuildId);
        if (guild?.GetChannel(scheduledCommand.ChannelId) is not ISocketMessageChannel channel)
            return;

        var message = await channel.GetMessageAsync(scheduledCommand.MessageId) as IUserMessage;
        var user = await (guild as IGuild).GetUserAsync(scheduledCommand.UserId);

        if (message is null || user is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await cmdHandler.TryRunCommand(guild,
                    channel,
                    new DoAsUserMessage(message, user, scheduledCommand.Text));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error running scheduled command {Id}", scheduledCommand.Id);
            }
        });
    }

    /// <summary>
    /// Adds a scheduled command to be executed after the specified time
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="channelId">ID of the channel where the command was issued</param>
    /// <param name="messageId">ID of the message that triggered this command</param>
    /// <param name="userId">ID of the user who scheduled the command</param>
    /// <param name="commandText">The command text to execute</param>
    /// <param name="when">Time span after which the command will be executed</param>
    /// <returns>True if command was added, false if user reached the limit</returns>
    public async Task<bool> AddScheduledCommandAsync(
        ulong guildId,
        ulong channelId,
        ulong messageId,
        ulong userId,
        string commandText,
        TimeSpan when)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText, nameof(commandText));

        await using var uow = db.GetDbContext();

        var count = await uow.GetTable<ScheduledCommand>()
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .CountAsyncLinqToDB();

        if (count >= ucs.Data.MaxScheduledPerUser)
            return false;

        await uow.GetTable<ScheduledCommand>()
            .InsertAsync(() => new()
            {
                GuildId = guildId,
                UserId = userId,
                Text = commandText,
                When = DateTime.UtcNow + when,
                ChannelId = channelId,
                MessageId = messageId
            });

        _tcs.TrySetResult();

        return true;
    }

    /// <summary>
    /// Gets all scheduled commands for a specific user in a guild
    /// </summary>
    /// <param name="guildId">Guild ID</param>
    /// <param name="userId">User ID</param>
    /// <returns>List of scheduled commands</returns>
    public async Task<List<ScheduledCommand>> GetUserScheduledCommandsAsync(ulong guildId, ulong userId)
    {
        await using var uow = db.GetDbContext();

        return await uow.GetTable<ScheduledCommand>()
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .OrderBy(x => x.When)
            .ToListAsyncLinqToDB();
    }

    /// <summary>
    /// Deletes a scheduled command by its ID
    /// </summary>
    /// <param name="id">ID of the scheduled command</param>
    /// <param name="guildId">Guild ID</param>
    /// <param name="userId">User ID</param>
    /// <returns>True if command was deleted, false otherwise</returns>
    public async Task<bool> DeleteScheduledCommandAsync(int id, ulong guildId, ulong userId)
    {
        await using var uow = db.GetDbContext();

        var result = await uow.GetTable<ScheduledCommand>()
            .Where(x => x.Id == id && x.GuildId == guildId && x.UserId == userId)
            .DeleteAsync();

        if (result > 0)
            _tcs.TrySetResult();

        return result > 0;
    }
}