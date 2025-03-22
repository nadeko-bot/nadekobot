using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Utility.Scheduled;

public sealed class ScheduleCommandService(
    DbService db,
    ICommandHandler cmdHandler,
    DiscordSocketClient client,
    ShardData shardData) : INService, IReadyExecutor
{
    private TaskCompletionSource _tcs = new();

    public async Task OnReadyAsync()
    {
        while (true)
        {
            _tcs = new();

            // get the next scheduled command
            var scheduledCommand = await db.GetDbContext()
                .GetTable<ScheduledCommand>()
                .Where(x => Queries.GuildOnShard(x.GuildId, shardData.TotalShards, shardData.ShardId))
                .OrderBy(x => x.When)
                .FirstOrDefaultAsyncLinqToDB();

            if (scheduledCommand is null)
            {
                await _tcs.Task;
                continue;
            }

            var now = DateTime.UtcNow;
            if (scheduledCommand.When > now)
            {
                var diff = scheduledCommand.When - now;
                await Task.WhenAny(Task.Delay(diff), _tcs.Task);
                continue;
            }

            await db.GetDbContext()
                .GetTable<ScheduledCommand>()
                .Where(x => x.Id == scheduledCommand.Id)
                .DeleteAsync();

            var guild = client.GetGuild(scheduledCommand.GuildId);
            var channel = guild?.GetChannel(scheduledCommand.ChannelId) as ISocketMessageChannel;

            if (guild is null || channel is null)
                continue;

            var message = await channel.GetMessageAsync(scheduledCommand.MessageId) as IUserMessage;
            var user = await (guild as IGuild).GetUserAsync(scheduledCommand.UserId);

            if (message is null || user is null)
                continue;

            _ = Task.Run(async ()
                => await cmdHandler.TryRunCommand(guild,
                    channel,
                    new DoAsUserMessage(message, user, scheduledCommand.Text)));
        }
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

        if (count >= 5)
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
            .AsNoTracking()
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