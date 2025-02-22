#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions.Services;

public sealed class FilterService : IExecOnMessage, IReadyExecutor
{
    public ConcurrentHashSet<ulong> InviteFilteringChannels { get; } = [];
    public ConcurrentHashSet<ulong> InviteFilteringServers { get; } = [];

    //serverid, filteredwords
    public ConcurrentDictionary<ulong, ConcurrentHashSet<string>> ServerFilteredWords { get; } = new();

    public ConcurrentHashSet<ulong> WordFilteringChannels { get; } = [];
    public ConcurrentHashSet<ulong> WordFilteringServers { get; } = [];

    public ConcurrentHashSet<ulong> LinkFilteringChannels { get; } = [];
    public ConcurrentHashSet<ulong> LinkFilteringServers { get; } = [];

    public int Priority
        => int.MaxValue - 1;

    private readonly DbService _db;
    private readonly ShardData _shardData;

    public FilterService(DiscordSocketClient client, DbService db, ShardData shardData)
    {
        _db = db;
        _shardData = shardData;

        client.MessageUpdated += (oldData, newMsg, channel) =>
        {
            _ = Task.Run(() =>
            {
                var guild = (channel as ITextChannel)?.Guild;

                if (guild is null || newMsg is not IUserMessage usrMsg)
                    return Task.CompletedTask;

                return ExecOnMessageAsync(guild, usrMsg);
            });
            return Task.CompletedTask;
        };
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        var confs = await uow.GetTable<GuildFilterConfig>()
                             .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                             .LoadWith(x => x.FilterInvitesChannelIds)
                             .LoadWith(x => x.FilterWordsChannelIds)
                             .LoadWith(x => x.FilterLinksChannelIds)
                             .LoadWith(x => x.FilteredWords)
                             .ToListAsyncLinqToDB();

        foreach (var conf in confs)
        {
            foreach (var c in conf.FilterInvitesChannelIds)
                InviteFilteringChannels.Add(c.ChannelId);

            foreach (var c in conf.FilterWordsChannelIds)
                WordFilteringChannels.Add(c.ChannelId);

            foreach (var c in conf.FilterLinksChannelIds)
                LinkFilteringChannels.Add(c.ChannelId);

            if (conf.FilterInvites)
                InviteFilteringServers.Add(conf.GuildId);

            if (conf.FilterWords)
                WordFilteringServers.Add(conf.GuildId);

            if (conf.FilterLinks)
                LinkFilteringServers.Add(conf.GuildId);

            foreach (var word in conf.FilteredWords)
                ServerFilteredWords.GetOrAdd(conf.GuildId, new ConcurrentHashSet<string>()).Add(word.Word);
        }
    }

    public ConcurrentHashSet<string> FilteredWordsForChannel(ulong channelId, ulong guildId)
    {
        var words = new ConcurrentHashSet<string>();
        if (WordFilteringChannels.Contains(channelId))
            ServerFilteredWords.TryGetValue(guildId, out words);
        return words;
    }

    public async Task ClearFilteredWords(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId,
            set => set.Include(x => x.FilteredWords)
                      .Include(x => x.FilterWordsChannelIds));

        WordFilteringServers.TryRemove(guildId);
        ServerFilteredWords.TryRemove(guildId, out _);

        foreach (var c in fc.FilterWordsChannelIds)
            WordFilteringChannels.TryRemove(c.ChannelId);

        fc.FilterWords = false;
        fc.FilteredWords.Clear();
        fc.FilterWordsChannelIds.Clear();

        await uow.SaveChangesAsync();
    }

    public ConcurrentHashSet<string> FilteredWordsForServer(ulong guildId)
    {
        var words = new ConcurrentHashSet<string>();
        if (WordFilteringServers.Contains(guildId))
            ServerFilteredWords.TryGetValue(guildId, out words);
        return words;
    }

    public async Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        if (msg.Author is not IGuildUser gu || gu.GuildPermissions.Administrator)
            return false;

        var results = await Task.WhenAll(FilterInvites(guild, msg), FilterWords(guild, msg), FilterLinks(guild, msg));

        return results.Any(x => x);
    }

    private async Task<bool> FilterWords(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is null)
            return false;
        if (usrMsg is null)
            return false;

        var filteredChannelWords =
            FilteredWordsForChannel(usrMsg.Channel.Id, guild.Id) ?? new ConcurrentHashSet<string>();
        var filteredServerWords = FilteredWordsForServer(guild.Id) ?? new ConcurrentHashSet<string>();
        var wordsInMessage = usrMsg.Content.ToLowerInvariant().Split(' ');
        if (filteredChannelWords.Count != 0 || filteredServerWords.Count != 0)
        {
            foreach (var word in wordsInMessage)
            {
                if (filteredChannelWords.Contains(word) || filteredServerWords.Contains(word))
                {
                    Log.Information("User {UserName} [{UserId}] used a filtered word in {ChannelId} channel",
                        usrMsg.Author.ToString(),
                        usrMsg.Author.Id,
                        usrMsg.Channel.Id);

                    try
                    {
                        await usrMsg.DeleteAsync();
                    }
                    catch (HttpException ex)
                    {
                        Log.Warning(ex,
                            "I do not have permission to filter words in channel with id {Id}",
                            usrMsg.Channel.Id);
                    }

                    return true;
                }
            }
        }

        return false;
    }

    private async Task<bool> FilterInvites(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is null)
            return false;
        if (usrMsg is null)
            return false;

        // if user has manage messages perm, don't filter
        if (usrMsg.Channel is ITextChannel ch && usrMsg.Author is IGuildUser gu && gu.GetPermissions(ch).ManageMessages)
            return false;

        if ((InviteFilteringChannels.Contains(usrMsg.Channel.Id) || InviteFilteringServers.Contains(guild.Id))
            && usrMsg.Content.IsDiscordInvite())
        {
            Log.Information("User {UserName} [{UserId}] sent a filtered invite to {ChannelId} channel",
                usrMsg.Author.ToString(),
                usrMsg.Author.Id,
                usrMsg.Channel.Id);

            try
            {
                await usrMsg.DeleteAsync();
                return true;
            }
            catch (HttpException ex)
            {
                Log.Warning(ex,
                    "I do not have permission to filter invites in channel with id {Id}",
                    usrMsg.Channel.Id);
                return true;
            }
        }

        return false;
    }

    private async Task<bool> FilterLinks(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is null)
            return false;
        if (usrMsg is null)
            return false;

        // if user has manage messages perm, don't filter
        if (usrMsg.Channel is ITextChannel ch && usrMsg.Author is IGuildUser gu && gu.GetPermissions(ch).ManageMessages)
            return false;

        if ((LinkFilteringChannels.Contains(usrMsg.Channel.Id) || LinkFilteringServers.Contains(guild.Id))
            && usrMsg.Content.TryGetUrlPath(out _))
        {
            Log.Information("User {UserName} [{UserId}] sent a filtered link to {ChannelId} channel",
                usrMsg.Author.ToString(),
                usrMsg.Author.Id,
                usrMsg.Channel.Id);

            try
            {
                await usrMsg.DeleteAsync();
                return true;
            }
            catch (HttpException ex)
            {
                Log.Warning(ex, "I do not have permission to filter links in channel with id {Id}", usrMsg.Channel.Id);
                return true;
            }
        }

        return false;
    }

    public async Task<ServerFilterSettings> GetFilterSettings(ulong guildId)
    {
        await using var uow = _db.GetDbContext();

        var conf = await uow.GetTable<GuildFilterConfig>()
                            .Where(fi => fi.GuildId == guildId)
                            .LoadWith(x => x.FilterInvitesChannelIds)
                            .LoadWith(x => x.FilterLinksChannelIds)
                            .FirstOrDefaultAsyncLinqToDB();

        return new()
        {
            FilterInvitesChannels = conf?.FilterInvitesChannelIds.Select(x => x.ChannelId).ToArray() ?? [],
            FilterLinksChannels = conf?.FilterLinksChannelIds.Select(x => x.ChannelId).ToArray() ?? [],
            FilterInvitesEnabled = false,
            FilterLinksEnabled = false,
        };
    }

    public async Task<bool> ToggleServerLinkFilteringAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();

        var fc = uow.FilterConfigForId(guildId);
        if (LinkFilteringServers.Add(guildId))
        {
            fc.FilterLinks = true;
        }
        else
        {
            fc.FilterLinks = false;
        }

        await uow.SaveChangesAsync();
        return fc.FilterLinks;
    }

    public async Task<bool> ToggleChannelLinkFilteringAsync(ulong guildId, ulong channelId)
    {
        await using var uow = _db.GetDbContext();

        var fc = uow.FilterConfigForId(guildId, set => set.Include(x => x.FilterLinksChannelIds));

        if (LinkFilteringChannels.Add(channelId))
        {
            fc.FilterLinksChannelIds.Add(new FilterLinksChannelId
            {
                ChannelId = channelId
            });

            await uow.SaveChangesAsync();
            return true;
        }

        LinkFilteringChannels.TryRemove(channelId);
        fc.FilterLinksChannelIds.RemoveWhere(x => x.ChannelId == channelId);
        await uow.SaveChangesAsync();
        return false;
    }

    public async Task<bool> ToggleServerInviteFilteringAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId);

        if (InviteFilteringServers.Add(guildId))
        {
            fc.FilterInvites = true;
            await uow.SaveChangesAsync();
            return true;
        }

        fc.FilterInvites = false;
        await uow.SaveChangesAsync();
        return false;
    }

    public async Task<bool> ToggleChannelInviteFilteringAsync(ulong guildId, ulong channelId)
    {
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId, set => set.Include(x => x.FilterInvitesChannelIds));

        if (InviteFilteringChannels.Add(channelId))
        {
            fc.FilterInvitesChannelIds.Add(new FilterChannelId()
            {
                ChannelId = channelId
            });

            await uow.SaveChangesAsync();
            return true;
        }

        InviteFilteringChannels.TryRemove(channelId);
        fc.FilterInvitesChannelIds.RemoveWhere(x => x.ChannelId == channelId);
        await uow.SaveChangesAsync();
        return false;
    }

    public async Task<bool> ToggleServerWordFilteringAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId);

        if (WordFilteringServers.Add(guildId))
        {
            fc.FilterWords = true;
            await uow.SaveChangesAsync();
            return true;
        }

        fc.FilterWords = false;
        await uow.SaveChangesAsync();
        return false;
    }

    public async Task<bool> ToggleChannelWordFilteringAsync(ulong guildId, ulong channelId)
    {
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId, set => set.Include(x => x.FilterWordsChannelIds));

        if (WordFilteringChannels.Add(channelId))
        {
            fc.FilterWordsChannelIds.Add(new FilterWordsChannelId()
            {
                ChannelId = channelId
            });

            await uow.SaveChangesAsync();
            return true;
        }

        WordFilteringChannels.TryRemove(channelId);
        fc.FilterWordsChannelIds.RemoveWhere(x => x.ChannelId == channelId);
        await uow.SaveChangesAsync();
        return false;
    }

    public async Task<bool> ToggleFilteredWordAsync(ulong guildId, string word)
    {
        word = word?.Trim().ToLowerInvariant();
        
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId, set => set.Include(x => x.FilteredWords));
        var sfw = ServerFilteredWords.GetOrAdd(guildId, []);
        if (sfw.Add(word))
        {
            fc.FilteredWords.Add(new FilteredWord()
            {
                Word = word
            });

            await uow.SaveChangesAsync();
            return true;
        }

        sfw.TryRemove(word);
        fc.FilteredWords.RemoveWhere(x => x.Word == word);
        await uow.SaveChangesAsync();

        return false;
    }
}