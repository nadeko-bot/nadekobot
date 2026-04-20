using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Threading.Channels;

namespace NadekoBot.Modules.Administration.Honeypot;

public sealed class HoneyPotService : IHoneyPotService, IReadyExecutor, IExecNoCommand, INService
{
    private readonly DbService _db;
    private readonly ILogCommandService _logService;

    private ConcurrentDictionary<ulong, HoneypotAction> _channels = new();

    private readonly record struct PunishmentEntry(SocketGuildUser User, HoneypotAction Action);

    private readonly Channel<PunishmentEntry> _punishments = Channel.CreateBounded<PunishmentEntry>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public HoneyPotService(DbService db, ILogCommandService logService)
    {
        _db = db;
        _logService = logService;
    }

    public async Task<bool> ToggleHoneypotChannel(ulong guildId, ulong channelId)
    {
        await using var uow = _db.GetDbContext();

        var deleted = await uow.HoneyPotChannels
                               .Where(x => x.GuildId == guildId)
                               .DeleteWithOutputAsync();

        if (deleted.Length > 0)
        {
            _channels.TryRemove(deleted[0].ChannelId, out _);
            return false;
        }

        await uow.HoneyPotChannels
                  .ToLinqToDBTable()
                  .InsertAsync(() => new HoneypotChannel
                  {
                      GuildId = guildId,
                      ChannelId = channelId,
                      Action = HoneypotAction.Softban,
                  });

        _channels[channelId] = HoneypotAction.Softban;

        return true;
    }

    public async Task SetHoneypotChannel(ulong guildId, ulong channelId, HoneypotAction action)
    {
        await using var uow = _db.GetDbContext();

        await uow.HoneyPotChannels
                  .Where(x => x.GuildId == guildId)
                  .DeleteAsync();

        await uow.HoneyPotChannels
                  .ToLinqToDBTable()
                  .InsertAsync(() => new HoneypotChannel
                  {
                      GuildId = guildId,
                      ChannelId = channelId,
                      Action = action,
                  });

        _channels[channelId] = action;
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        var channels = await uow.HoneyPotChannels
                                .Select(x => new { x.ChannelId, x.Action })
                                .ToListAsyncLinqToDB();

        _channels = new(channels.Select(x => KeyValuePair.Create(x.ChannelId, x.Action)));

        while (await _punishments.Reader.WaitToReadAsync())
        {
            while (_punishments.Reader.TryRead(out var entry))
            {
                try
                {
                    Log.Information("Honeypot caught user {User} [{UserId}], action: {Action}",
                        entry.User, entry.User.Id, entry.Action);

                    _logService.AddBanIgnore(entry.User.Guild.Id, entry.User.Id);
                    await entry.User.BanAsync(pruneDays: 1, reason: "Honeypot");

                    if (entry.Action is HoneypotAction.Softban)
                    {
                        _logService.AddUnbanIgnore(entry.User.Guild.Id, entry.User.Id);
                        await entry.User.Guild.RemoveBanAsync(entry.User.Id);
                    }

                    await _logService.LogHoneypot(entry.User.Guild, entry.User);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Failed banning {User} due to {Error}", entry.User, e.Message);
                }

                await Task.Delay(1000);
            }
        }
    }

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (_channels.TryGetValue(msg.Channel.Id, out var action) && msg.Author is SocketGuildUser sgu)
        {
            if (!sgu.GuildPermissions.BanMembers)
                await _punishments.Writer.WriteAsync(new(sgu, action));
        }
    }
}