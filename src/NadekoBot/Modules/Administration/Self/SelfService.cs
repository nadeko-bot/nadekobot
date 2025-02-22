#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Collections.Immutable;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Modules.Administration.Services;

public sealed class SelfService : IExecNoCommand, IReadyExecutor, INService
{
    private readonly CommandHandler _cmdHandler;
    private readonly DbService _db;
    private readonly IBotStrings _strings;
    private readonly DiscordSocketClient _client;

    private readonly IBotCreds _creds;

    private ImmutableDictionary<ulong, IDMChannel> ownerChannels =
        new Dictionary<ulong, IDMChannel>().ToImmutableDictionary();

    private ConcurrentDictionary<ulong?, ConcurrentDictionary<int, Timer>> autoCommands = new();

    private readonly IHttpClientFactory _httpFactory;
    private readonly BotConfigService _bss;
    private readonly IPubSub _pubSub;
    private readonly IMessageSenderService _sender;

    //keys
    private readonly TypedKey<string> _guildLeaveKey;

    public SelfService(
        DiscordSocketClient client,
        CommandHandler cmdHandler,
        DbService db,
        IBotStrings strings,
        IBotCreds creds,
        IHttpClientFactory factory,
        BotConfigService bss,
        IPubSub pubSub,
        IMessageSenderService sender)
    {
        _cmdHandler = cmdHandler;
        _db = db;
        _strings = strings;
        _client = client;
        _creds = creds;
        _httpFactory = factory;
        _bss = bss;
        _pubSub = pubSub;
        _sender = sender;
        _guildLeaveKey = new("guild.leave");

        _pubSub.Sub(_guildLeaveKey,
            async input =>
            {
                var guildStr = input.ToString().Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(guildStr))
                    return;

                var server = _client.Guilds.FirstOrDefault(g => g.Id.ToString() == guildStr
                                                                || g.Name.Trim().ToUpperInvariant() == guildStr);
                if (server is null)
                    return;

                if (server.OwnerId != _client.CurrentUser.Id)
                {
                    await server.LeaveAsync();
                }
                else
                {
                    await server.DeleteAsync();
                    Log.Information("Deleted server {Name} [{Id}]", server.Name, server.Id);
                }
            });
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        autoCommands = uow.Set<AutoCommand>()
                          .AsNoTracking()
                          .Where(x => x.Interval >= 5)
                          .AsEnumerable()
                          .GroupBy(x => x.GuildId)
                          .ToDictionary(x => x.Key,
                              y => y.ToDictionary(x => x.Id, TimerFromAutoCommand).ToConcurrent())
                          .ToConcurrent();

        var startupCommands = uow.Set<AutoCommand>().AsNoTracking().Where(x => x.Interval == 0);
        foreach (var cmd in startupCommands)
        {
            try
            {
                await ExecuteCommand(cmd);
            }
            catch
            {
            }
        }

        if (_client.ShardId == 0)
            await LoadOwnerChannels();
    }

    private Timer TimerFromAutoCommand(AutoCommand x)
        => new(async obj => await ExecuteCommand((AutoCommand)obj), x, x.Interval * 1000, x.Interval * 1000);

    private async Task ExecuteCommand(AutoCommand cmd)
    {
        try
        {
            if (cmd.GuildId is null)
                return;

            var guildShard = (int)((cmd.GuildId.Value >> 22) % (ulong)_creds.TotalShards);
            if (guildShard != _client.ShardId)
                return;
            var prefix = _cmdHandler.GetPrefix(cmd.GuildId);
            //if someone already has .die as their startup command, ignore it
            if (cmd.CommandText.StartsWith(prefix + "die", StringComparison.InvariantCulture))
                return;
            await _cmdHandler.ExecuteExternal(cmd.GuildId, cmd.ChannelId, cmd.CommandText);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in SelfService ExecuteCommand");
        }
    }

    public void AddNewAutoCommand(AutoCommand cmd)
    {
        using (var uow = _db.GetDbContext())
        {
            uow.Set<AutoCommand>().Add(cmd);
            uow.SaveChanges();
        }

        if (cmd.Interval >= 5)
        {
            var autos = autoCommands.GetOrAdd(cmd.GuildId, new ConcurrentDictionary<int, Timer>());
            autos.AddOrUpdate(cmd.Id,
                _ => TimerFromAutoCommand(cmd),
                (_, old) =>
                {
                    old.Change(Timeout.Infinite, Timeout.Infinite);
                    return TimerFromAutoCommand(cmd);
                });
        }
    }

    public IEnumerable<AutoCommand> GetStartupCommands()
    {
        using var uow = _db.GetDbContext();
        return uow.Set<AutoCommand>().AsNoTracking().Where(x => x.Interval == 0).OrderBy(x => x.Id).ToList();
    }

    public IEnumerable<AutoCommand> GetAutoCommands()
    {
        using var uow = _db.GetDbContext();
        return uow.Set<AutoCommand>().AsNoTracking().Where(x => x.Interval >= 5).OrderBy(x => x.Id).ToList();
    }

    private async Task LoadOwnerChannels()
    {
        var channels = await _creds.OwnerIds.Select(id =>
                                   {
                                       var user = _client.GetUser(id);
                                       if (user is null)
                                           return Task.FromResult<IDMChannel>(null);

                                       return user.CreateDMChannelAsync();
                                   })
                                   .WhenAll();

        ownerChannels = channels.Where(x => x is not null)
                                .ToDictionary(x => x.Recipient.Id, x => x)
                                .ToImmutableDictionary();

        if (!ownerChannels.Any())
        {
            Log.Warning(
                "No owner channels created! Make sure you've specified the correct OwnerId in the creds.yml file and invited the bot to a Discord server");
        }
        else
        {
            Log.Information("Created {OwnerChannelCount} out of {TotalOwnerChannelCount} owner message channels",
                ownerChannels.Count,
                _creds.OwnerIds.Count);
        }
    }

    public Task LeaveGuild(string guildStr)
        => _pubSub.Pub(_guildLeaveKey, guildStr);

    // forwards dms
    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        var bs = _bss.Data;
        if (msg.Channel is IDMChannel && bs.ForwardMessages && (ownerChannels.Any() || bs.ForwardToChannel is not null))
        {
            var title = _strings.GetText(strs.dm_from) + $" [{msg.Author}]({msg.Author.Id})";

            var attachamentsTxt = _strings.GetText(strs.attachments);

            var toSend = msg.Content;

            if (msg.Attachments.Count > 0)
            {
                toSend += $"\n\n{Format.Code(attachamentsTxt)}:\n"
                          + string.Join("\n", msg.Attachments.Select(a => a.ProxyUrl));
            }

            if (bs.ForwardToAllOwners)
            {
                var allOwnerChannels = ownerChannels.Values;

                foreach (var ownerCh in allOwnerChannels.Where(ch => ch.Recipient.Id != msg.Author.Id))
                {
                    try
                    {
                        await _sender.Response(ownerCh).Confirm(title, toSend).SendAsync();
                    }
                    catch
                    {
                        Log.Warning("Can't contact owner with id {OwnerId}", ownerCh.Recipient.Id);
                    }
                }
            }
            else if (bs.ForwardToChannel is ulong cid)
            {
                try
                {
                    if (_client.GetChannel(cid) is ITextChannel ch)
                        await _sender.Response(ch).Confirm(title, toSend).SendAsync();
                }
                catch
                {
                    Log.Warning("Error forwarding message to the channel");
                }
            }
            else
            {
                var firstOwnerChannel = ownerChannels.Values.First();
                if (firstOwnerChannel.Recipient.Id != msg.Author.Id)
                {
                    try
                    {
                        await _sender.Response(firstOwnerChannel).Confirm(title, toSend).SendAsync();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }
    }

    public bool RemoveStartupCommand(int index, out AutoCommand cmd)
    {
        using var uow = _db.GetDbContext();
        cmd = uow.Set<AutoCommand>().AsNoTracking().Where(x => x.Interval == 0).Skip(index).FirstOrDefault();

        if (cmd is not null)
        {
            uow.Remove(cmd);
            uow.SaveChanges();
            return true;
        }

        return false;
    }

    public bool RemoveAutoCommand(int index, out AutoCommand cmd)
    {
        using var uow = _db.GetDbContext();
        cmd = uow.Set<AutoCommand>().AsNoTracking().Where(x => x.Interval >= 5).Skip(index).FirstOrDefault();

        if (cmd is not null)
        {
            uow.Remove(cmd);
            if (autoCommands.TryGetValue(cmd.GuildId, out var autos))
            {
                if (autos.TryRemove(cmd.Id, out var timer))
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            uow.SaveChanges();
            return true;
        }

        return false;
    }

    public async Task<bool> SetAvatar(string img)
    {
        if (string.IsNullOrWhiteSpace(img))
            return false;

        if (!Uri.IsWellFormedUriString(img, UriKind.Absolute))
            return false;

        var uri = new Uri(img);

        using var http = _httpFactory.CreateClient();
        using var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (!sr.IsImage())
            return false;

        // i can't just do ReadAsStreamAsync because dicord.net's image poops itself
        var imgData = await sr.Content.ReadAsByteArrayAsync();
        await using var imgStream = imgData.ToStream();
        await _client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(imgStream));

        return true;
    }

    public async Task<bool> SetBanner(string img)
    {
        if (string.IsNullOrWhiteSpace(img))
        {
            return false;
        }

        if (!Uri.IsWellFormedUriString(img, UriKind.Absolute))
        {
            return false;
        }

        var uri = new Uri(img);

        using var http = _httpFactory.CreateClient();
        using var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        if (!sr.IsImage())
        {
            return false;
        }

        if (sr.GetContentLength() > 8.Megabytes())
        {
            return false;
        }

        await using var imageStream = await sr.Content.ReadAsStreamAsync();

        await _client.CurrentUser.ModifyAsync(x => x.Banner = new Image(imageStream));
        return true;
    }


    public void ClearStartupCommands()
    {
        using var uow = _db.GetDbContext();
        var toRemove = uow.Set<AutoCommand>().AsNoTracking().Where(x => x.Interval == 0);

        uow.Set<AutoCommand>().RemoveRange(toRemove);
        uow.SaveChanges();
    }

    public bool ForwardMessages()
    {
        var isForwarding = false;
        _bss.ModifyConfig(config => { isForwarding = config.ForwardMessages = !config.ForwardMessages; });

        return isForwarding;
    }

    public bool ForwardToAll()
    {
        var isToAll = false;
        _bss.ModifyConfig(config => { isToAll = config.ForwardToAllOwners = !config.ForwardToAllOwners; });
        return isToAll;
    }

    public bool ForwardToChannel(ulong? channelId)
    {
        using var uow = _db.GetDbContext();

        _bss.ModifyConfig(config =>
        {
            config.ForwardToChannel = channelId == config.ForwardToChannel
                ? null
                : channelId;
        });

        return channelId is not null;
    }

    /// <summary>
    /// Adds the specified <paramref name="users"/> to the database. If a database user with placeholder name
    /// and discriminator is present in <paramref name="users"/>, their name and discriminator get updated accordingly.
    /// </summary>
    /// <param name="ctx">This database context.</param>
    /// <param name="users">The users to add or update in the database.</param>
    /// <returns>A tuple with the amount of new users added and old users updated.</returns>
    public async Task<(long UsersAdded, long UsersUpdated)> RefreshUsersAsync(List<IUser> users)
    {
        await using var ctx = _db.GetDbContext();
        var presentDbUsers = await ctx.GetTable<DiscordUser>()
                                      .Select(x => new
                                      {
                                          x.UserId,
                                          x.Username,
                                      })
                                      .Where(x => users.Select(y => y.Id).Contains(x.UserId))
                                      .ToArrayAsyncEF();

        var usersToAdd = users
                         .Where(x => !presentDbUsers.Select(x => x.UserId).Contains(x.Id))
                         .Select(x => new DiscordUser()
                         {
                             UserId = x.Id,
                             AvatarId = x.AvatarId,
                             Username = x.Username,
                         });

        var added = (await ctx.BulkCopyAsync(usersToAdd)).RowsCopied;
        var toUpdateUserIds = presentDbUsers
                              .Where(x => x.Username.StartsWith("??"))
                              .Select(x => x.UserId)
                              .ToArray();

        foreach (var user in users.Where(x => toUpdateUserIds.Contains(x.Id)))
        {
            await ctx.GetTable<DiscordUser>()
                     .Where(x => x.UserId == user.Id)
                     .UpdateAsync(x => new DiscordUser()
                     {
                         Username = user.Username,

                         // .award tends to set AvatarId and DateAdded to NULL, so account for that.
                         AvatarId = user.AvatarId,
                         DateAdded = x.DateAdded ?? DateTime.UtcNow
                     });
        }

        return (added, toUpdateUserIds.Length);
    }
}