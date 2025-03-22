#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.Services;

public class VerboseErrorsService : IReadyExecutor, INService
{
    private readonly ConcurrentHashSet<ulong> _guildsDisabled = [];
    private readonly DbService _db;
    private readonly CommandHandler _ch;
    private readonly ICommandsUtilityService _hs;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;

    public VerboseErrorsService(
        DbService db,
        CommandHandler ch,
        IMessageSenderService sender,
        ICommandsUtilityService hs,
        ShardData shardData)
    {
        _db = db;
        _ch = ch;
        _hs = hs;
        _sender = sender;
        _shardData = shardData;
    }

    private async Task LogVerboseError(CommandInfo cmd, ITextChannel channel, string reason)
    {
        if (channel is null || _guildsDisabled.Contains(channel.GuildId))
            return;

        try
        {
            var embed = _hs.GetCommandHelp(cmd, channel.Guild)
                .WithTitle("Command Error")
                .WithDescription(reason)
                .WithFooter("Admin may disable verbose errors via `.ve` command")
                .WithErrorColor();

            await _sender.Response(channel).Embed(embed).SendAsync();
        }
        catch
        {
            Log.Information("Verbose error wasn't able to be sent to the server: {GuildId}",
                channel.GuildId);
        }
    }

    /// <summary>
    /// Toggles or sets verbose errors for the specified guild.
    /// </summary>
    /// <param name="guildId">The ID of the guild to toggle verbose errors for.</param>
    /// <param name="maybeEnabled">If specified, sets to this value; otherwise toggles current value.</param>
    /// <returns>Returns the new state of verbose errors (true = enabled, false = disabled).</returns>
    public async Task<bool> ToggleVerboseErrors(ulong guildId, bool? maybeEnabled = null)
    {
        await using var ctx = _db.GetDbContext();
        
        var current = await ctx.GetTable<GuildConfig>()
            .Where(x => x.GuildId == guildId)
            .Select(x => x.VerboseErrors)
            .FirstOrDefaultAsync();
            
        var newState = maybeEnabled ?? !current;
        
        await ctx.GetTable<GuildConfig>()
            .Where(x => x.GuildId == guildId)
            .Set(x => x.VerboseErrors, newState)
            .UpdateAsync();
            
        if (newState)
        {
            _guildsDisabled.TryRemove(guildId);
        }
        else
        {
            _guildsDisabled.Add(guildId);
        }
        
        return newState;
    }

    public async Task OnReadyAsync()
    {
        await using var ctx = _db.GetDbContext();
        var disabledOn = ctx.GetTable<GuildConfig>()
            .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId) && !x.VerboseErrors)
            .Select(x => x.GuildId);

        foreach (var guildId in disabledOn)
            _guildsDisabled.Add(guildId);

        _ch.CommandErrored += LogVerboseError;
    }
}