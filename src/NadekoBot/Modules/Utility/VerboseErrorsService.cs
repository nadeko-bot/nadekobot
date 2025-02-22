#nullable disable
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

    public async Task<bool> ToggleVerboseErrors(ulong guildId, bool? maybeEnabled = null)
    {
        await using var ctx = _db.GetDbContext();

        var isEnabled = ctx.GetTable<GuildConfig>()
                            .Where(x => x.GuildId == guildId)
                            .Select(x => x.VerboseErrors)
                            .FirstOrDefault();

        if (isEnabled) // This doesn't need to be duplicated inside the using block
        {
            _guildsDisabled.TryRemove(guildId);
        }
        else
        {
            _guildsDisabled.Add(guildId);
        }

        return isEnabled;
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