#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using System.Net;
using System.Threading.Channels;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public sealed class SomethingOnlyChannelService : IExecOnMessage
{
    public int Priority { get; } = 0;
    private readonly IMemoryCache _ticketCache;
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> _imageOnly;
    private readonly ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> _linkOnly;

    private readonly Channel<IUserMessage> _deleteQueue = Channel.CreateBounded<IUserMessage>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });


    public SomethingOnlyChannelService(IMemoryCache ticketCache, DiscordSocketClient client, DbService db)
    {
        _ticketCache = ticketCache;
        _client = client;
        _db = db;

        using var uow = _db.GetDbContext();
        _imageOnly = uow.Set<ImageOnlyChannel>()
            .Where(x => x.Type == OnlyChannelType.Image)
            .ToList()
            .GroupBy(x => x.GuildId)
            .ToDictionary(x => x.Key, x => new ConcurrentHashSet<ulong>(x.Select(y => y.ChannelId)))
            .ToConcurrent();

        _linkOnly = uow.Set<ImageOnlyChannel>()
            .Where(x => x.Type == OnlyChannelType.Link)
            .ToList()
            .GroupBy(x => x.GuildId)
            .ToDictionary(x => x.Key, x => new ConcurrentHashSet<ulong>(x.Select(y => y.ChannelId)))
            .ToConcurrent();
        
        _ = Task.Run(DeleteQueueRunner);

        _client.ChannelDestroyed += ClientOnChannelDestroyed;
    }

    private async Task ClientOnChannelDestroyed(SocketChannel ch)
    {
        if (ch is not IGuildChannel gch)
            return;

        if (GetMode(gch.GuildId, ch.Id) is not null)
            await DisableAsync(gch.GuildId, ch.Id);
    }

    private async Task DeleteQueueRunner()
    {
        while (true)
        {
            var toDelete = await _deleteQueue.Reader.ReadAsync();
            try
            {
                await toDelete.DeleteAsync();
                await Task.Delay(1000);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                // disable if bot can't delete messages in the channel
                await DisableAsync(((ITextChannel)toDelete.Channel).GuildId, toDelete.Channel.Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error deleting a message in an image-only or link-only channel");
            }
        }
    }

    private ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> GetSet(OnlyChannelType type)
        => type == OnlyChannelType.Image ? _imageOnly : _linkOnly;

    public OnlyChannelType? GetMode(ulong guildId, ulong channelId)
    {
        if (_imageOnly.TryGetValue(guildId, out var chs) && chs.Contains(channelId))
            return OnlyChannelType.Image;

        if (_linkOnly.TryGetValue(guildId, out chs) && chs.Contains(channelId))
            return OnlyChannelType.Link;

        return null;
    }

    // returns true when the channel now has the specified type
    public async Task<bool> ToggleAsync(ulong guildId, ulong channelId, OnlyChannelType type)
    {
        if (GetMode(guildId, channelId) == type)
        {
            await DisableAsync(guildId, channelId);
            return false;
        }

        await using (var uow = _db.GetDbContext())
        {
            await using var tx = await uow.Database.BeginTransactionAsync();

            await uow.GetTable<ImageOnlyChannel>()
                .Where(x => x.ChannelId == channelId)
                .DeleteAsync();

            await uow.GetTable<ImageOnlyChannel>()
                .InsertAsync(() => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    Type = type,
                    DateAdded = DateTime.UtcNow
                });

            await tx.CommitAsync();
        }

        var other = type == OnlyChannelType.Image ? OnlyChannelType.Link : OnlyChannelType.Image;
        if (GetSet(other).TryGetValue(guildId, out var otherChannels))
            otherChannels.TryRemove(channelId);

        GetSet(type).GetOrAdd(guildId, static _ => new()).Add(channelId);
        return true;
    }

    public async Task DisableAsync(ulong guildId, ulong channelId)
    {
        if (_imageOnly.TryGetValue(guildId, out var chs))
            chs.TryRemove(channelId);

        if (_linkOnly.TryGetValue(guildId, out chs))
            chs.TryRemove(channelId);

        await using var uow = _db.GetDbContext();
        await uow.GetTable<ImageOnlyChannel>()
            .Where(x => x.ChannelId == channelId)
            .DeleteAsync();
    }

#nullable enable
    public async ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
#nullable disable
    {
        if (msg.Channel is not ITextChannel tch)
            return false;

        if (_imageOnly.TryGetValue(tch.GuildId, out var chs) && chs.Contains(msg.Channel.Id))
            return await HandleOnlyChannel(tch, msg, OnlyChannelType.Image);
        
        if (_linkOnly.TryGetValue(tch.GuildId, out chs) && chs.Contains(msg.Channel.Id))
            return await HandleOnlyChannel(tch, msg, OnlyChannelType.Link);

        return false;
    }

    private async Task<bool> HandleOnlyChannel(ITextChannel tch, IUserMessage msg, OnlyChannelType type)
    {
        if (type == OnlyChannelType.Image)
        {
            if (msg.Attachments.Any(x => x is { Height: > 0, Width: > 0 }))
                return false;
        }
        else
        {
            if (msg.Content.TryGetUrlPath(out _))
                return false;
        }
        
        var user = await tch.Guild.GetUserAsync(msg.Author.Id)
                   ?? await _client.Rest.GetGuildUserAsync(tch.GuildId, msg.Author.Id);

        if (user is null)
            return false;

        // ignore owner and admin
        if (user.Id == tch.Guild.OwnerId || user.GuildPermissions.Administrator)
        {
            Log.Information("{Type}-Only Channel: Ignoring owner or admin ({ChannelId})", type, msg.Channel.Id);
            return false;
        }

        // ignore users higher in hierarchy
        var botUser = await tch.Guild.GetCurrentUserAsync();
        if (user.GetRoles().Max(x => x.Position) >= botUser.GetRoles().Max(x => x.Position))
            return false;

        // manage roles is the channel's Manage Permissions, needed for the overwrite which stops the user
        var botPerms = botUser.GetPermissions(tch);
        if (!botPerms.ManageChannel || !botPerms.ManageRoles)
        {
            await DisableAsync(tch.GuildId, tch.Id);
            return false;
        }

        var shouldLock = AddUserTicket(tch.GuildId, msg.Author.Id);
        if (shouldLock)
        {
            try
            {
                await tch.AddPermissionOverwriteAsync(msg.Author, new(sendMessages: PermValue.Deny));
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                await DisableAsync(tch.GuildId, tch.Id);
                return false;
            }

            Log.Warning("{Type}-Only Channel: User {User} [{UserId}] has been banned from typing in the channel [{ChannelId}]",
                type,
                msg.Author,
                msg.Author.Id,
                msg.Channel.Id);
        }

        try
        {
            await _deleteQueue.Writer.WriteAsync(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting message {MessageId} in image-only channel {ChannelId}", msg.Id, tch.Id);
        }

        return true;
    }

    private bool AddUserTicket(ulong guildId, ulong userId)
    {
        var old = _ticketCache.GetOrCreate($"{guildId}_{userId}",
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return 0;
            });

        _ticketCache.Set($"{guildId}_{userId}", ++old);

        // if this is the third time that the user posts a
        // non image in an image-only channel on this server 
        return old > 2;
    }
}