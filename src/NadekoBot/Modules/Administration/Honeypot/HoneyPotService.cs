using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Threading.Channels;

namespace NadekoBot.Modules.Administration.Honeypot;

public sealed class HoneyPotService : IHoneyPotService, IReadyExecutor, IExecNoCommand, INService
{
    private readonly DbService _db;
    private readonly CommandHandler _handler;

    private ConcurrentHashSet<ulong> _channels = new();

    private Channel<SocketGuildUser> _punishments = Channel.CreateBounded<SocketGuildUser>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public HoneyPotService(DbService db, CommandHandler handler)
    {
        _db = db;
        _handler = handler;
    }

    public async Task<bool> ToggleHoneypotChannel(ulong guildId, ulong channelId)
    {
        await using var uow = _db.GetDbContext();

        var deleted = await uow.HoneyPotChannels
                               .Where(x => x.GuildId == guildId)
                               .DeleteWithOutputAsync();

        if (deleted.Length > 0)
        {
            _channels.TryRemove(deleted[0].ChannelId);
            return false;
        }

        await uow.HoneyPotChannels
                 .ToLinqToDBTable()
                 .InsertAsync(() => new HoneypotChannel
                 {
                     GuildId = guildId,
                     ChannelId = channelId
                 });

        _channels.Add(channelId);

        return true;
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        var channels = await uow.HoneyPotChannels
                                .Select(x => x.ChannelId)
                                .ToListAsyncLinqToDB();

        _channels = new(channels);

        while (await _punishments.Reader.WaitToReadAsync())
        {
            while (_punishments.Reader.TryRead(out var user))
            {
                try
                {
                    Log.Information("Honeypot caught user {User} [{UserId}]", user, user.Id);
                    await user.BanAsync(pruneDays: 1, reason: "Honeypot");
                    await user.Guild.RemoveBanAsync(user.Id);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Failed banning {User} due to {Error}", user, e.Message);
                }

                await Task.Delay(1000);
            }
        }
    }

    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        if (_channels.Contains(msg.Channel.Id) && msg.Author is SocketGuildUser sgu)
        {
            if (!sgu.GuildPermissions.BanMembers)
                await _punishments.Writer.WriteAsync(sgu);
        }
    }
}