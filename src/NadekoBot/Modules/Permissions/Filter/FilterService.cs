#nullable disable
using System.Collections.Frozen;
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
    private readonly ConcurrentDictionary<ulong, FilteredWordSet> _serverFilteredWords = new();

    public ConcurrentHashSet<ulong> WordFilteringChannels { get; } = [];
    public ConcurrentHashSet<ulong> WordFilteringServers { get; } = [];

    public ConcurrentHashSet<ulong> LinkFilteringChannels { get; } = [];
    public ConcurrentHashSet<ulong> LinkFilteringServers { get; } = [];

    public const int MAX_ALLOWED_DOMAINS = 50;

    private readonly ConcurrentDictionary<ulong, AllowedDomainSet> _allowedDomains = new();
    private long _allowedDomainsVersion;

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
            _ = Task.Run(async () =>
            {
                var guild = (channel as ITextChannel)?.Guild;

                if (guild is null || newMsg is not IUserMessage usrMsg)
                    return;

                await ExecOnMessageAsync(guild, usrMsg);
            });
            return Task.CompletedTask;
        };
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        var confs = await uow.GetTable<GuildFilterConfig>()
            .Where(Queries.GuildOnShard<GuildFilterConfig>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
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

            if (conf.FilteredWords.Count > 0)
            {
                var fws = _serverFilteredWords.GetOrAdd(conf.GuildId, static _ => new());
                fws.Bulk(conf.FilteredWords.Select(static x => x.Word));
            }
        }

        var version = Interlocked.Increment(ref _allowedDomainsVersion);
        var domains = await uow.GetTable<AllowedLinkDomain>()
            .Where(Queries.GuildOnShard<AllowedLinkDomain>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
            .Select(x => new { x.GuildId, x.Domain })
            .ToListAsyncLinqToDB();

        foreach (var group in domains.GroupBy(static x => x.GuildId))
        {
            StoreAllowedDomainsInternal(group.Key,
                new(version, group.Select(static x => x.Domain).ToFrozenSet(StringComparer.OrdinalIgnoreCase)));
        }
    }

    public HashSet<string> FilteredWordsForChannel(ulong channelId, ulong guildId)
    {
        if (WordFilteringChannels.Contains(channelId)
            && _serverFilteredWords.TryGetValue(guildId, out var fws))
            return fws.Snapshot;

        return null;
    }

    public async Task ClearFilteredWords(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId,
            set => set.Include(x => x.FilteredWords)
                .Include(x => x.FilterWordsChannelIds));

        WordFilteringServers.TryRemove(guildId);
        _serverFilteredWords.TryRemove(guildId, out _);

        foreach (var c in fc.FilterWordsChannelIds)
            WordFilteringChannels.TryRemove(c.ChannelId);

        fc.FilterWords = false;
        fc.FilteredWords.Clear();
        fc.FilterWordsChannelIds.Clear();

        await uow.SaveChangesAsync();
    }

    public HashSet<string> FilteredWordsForServer(ulong guildId)
    {
        if (WordFilteringServers.Contains(guildId)
            && _serverFilteredWords.TryGetValue(guildId, out var fws))
            return fws.Snapshot;

        return null;
    }

#nullable enable
    public async ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
#nullable disable
    {
        if (guild is null || msg.Author is not IGuildUser gu)
            return false;

        // Most servers have no filter, and the permissions of a member come from all of their roles.
        if (!HasAnyFilter(guild.Id, msg.Channel.Id) || gu.GuildPermissions.Administrator)
            return false;

        var invites = FilterInvites(guild, msg);
        var words = FilterWords(guild, msg);
        var links = FilterLinks(guild, msg);

        if (invites || words || links)
        {
            try
            {
                await msg.DeleteAsync();
            }
            catch (HttpException)
            {
            }

            return true;
        }

        return false;
    }

    private bool HasAnyFilter(ulong guildId, ulong channelId)
        => InviteFilteringServers.Contains(guildId)
           || WordFilteringServers.Contains(guildId)
           || LinkFilteringServers.Contains(guildId)
           || InviteFilteringChannels.Contains(channelId)
           || WordFilteringChannels.Contains(channelId)
           || LinkFilteringChannels.Contains(channelId);

    private bool FilterWords(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is null || usrMsg is null)
            return false;

        var filteredChannelWords = FilteredWordsForChannel(usrMsg.Channel.Id, guild.Id);
        var filteredServerWords = FilteredWordsForServer(guild.Id);

        if ((filteredChannelWords is null || filteredChannelWords.Count == 0)
            && (filteredServerWords is null || filteredServerWords.Count == 0))
            return false;

        if (ContainsFilteredWord(usrMsg.Content, filteredChannelWords, filteredServerWords))
            return true;

        var forwardedContent = usrMsg.ForwardedMessages.FirstOrDefault().Message?.Content;
        if (forwardedContent is not null
            && ContainsFilteredWord(forwardedContent, filteredChannelWords, filteredServerWords))
            return true;

        return false;

        bool ContainsFilteredWord(
            string content,
            HashSet<string> channelWords,
            HashSet<string> serverWords)
        {
            var hasChannelWords = channelWords is { Count: > 0 };
            var hasServerWords = serverWords is { Count: > 0 };
            var channelLookup = hasChannelWords ? channelWords.GetAlternateLookup<ReadOnlySpan<char>>() : default;
            var serverLookup = hasServerWords ? serverWords.GetAlternateLookup<ReadOnlySpan<char>>() : default;

            var span = content.AsSpan();
            foreach (var range in span.Split(' '))
            {
                var word = span[range];
                if (word.IsEmpty)
                    continue;

                if ((hasChannelWords && channelLookup.Contains(word))
                    || (hasServerWords && serverLookup.Contains(word)))
                {
                    Log.Information("User {UserName} [{UserId}] used a filtered word in {ChannelId} channel",
                        usrMsg.Author.ToString(),
                        usrMsg.Author.Id,
                        usrMsg.Channel.Id);

                    return true;
                }
            }

            return false;
        }
    }

    private bool FilterInvites(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is null)
            return false;
        if (usrMsg is null)
            return false;

        if (!InviteFilteringChannels.Contains(usrMsg.Channel.Id) && !InviteFilteringServers.Contains(guild.Id))
            return false;

        if (usrMsg.Channel is ITextChannel ch && usrMsg.Author is IGuildUser gu && gu.GetPermissions(ch).ManageMessages)
            return false;

        if (usrMsg.Content.IsDiscordInvite() || ForwardedHasInvite(usrMsg.ForwardedMessages))
        {
            Log.Information("User {UserName} [{UserId}] sent a filtered invite to {ChannelId} channel",
                usrMsg.Author.ToString(),
                usrMsg.Author.Id,
                usrMsg.Channel.Id);

            return true;
        }

        return false;
    }

    private bool FilterLinks(IGuild guild, IUserMessage usrMsg)
    {
        if (guild is null)
            return false;
        if (usrMsg is null)
            return false;

        if (!LinkFilteringChannels.Contains(usrMsg.Channel.Id) && !LinkFilteringServers.Contains(guild.Id))
            return false;

        if (usrMsg.Channel is ITextChannel ch && usrMsg.Author is IGuildUser gu && gu.GetPermissions(ch).ManageMessages)
            return false;

        var allowed = _allowedDomains.TryGetValue(guild.Id, out var set) ? set.Domains : null;
        if (LinkHosts.HasBlockedLink(usrMsg.Content, allowed) || ForwardedHasBlockedLink(usrMsg.ForwardedMessages, allowed))
        {
            Log.Information("User {UserName} [{UserId}] sent a filtered link to {ChannelId} channel",
                usrMsg.Author.ToString(),
                usrMsg.Author.Id,
                usrMsg.Channel.Id);

            return true;
        }

        return false;
    }

    private static bool ForwardedHasInvite(IReadOnlyCollection<MessageSnapshot> forwards)
    {
        foreach (var f in forwards)
            if (f.Message?.Content.IsDiscordInvite() == true)
                return true;
        return false;
    }

    private static bool ForwardedHasBlockedLink(IReadOnlyCollection<MessageSnapshot> forwards, FrozenSet<string> allowed)
    {
        foreach (var f in forwards)
        {
            if (f.Message?.Content is { } content && LinkHosts.HasBlockedLink(content, allowed))
                return true;
        }

        return false;
    }

    // Adds the domain, or removes it when the server already allows it.
    public async Task<LinkAllowResult> ToggleAllowedLinkDomainAsync(ulong guildId, string domain)
    {
        var result = await ToggleAllowedLinkDomainAsync(_db, guildId, domain);
        if (result != LinkAllowResult.LimitReached)
            await ReloadAllowedDomainsInternalAsync(guildId);

        return result;
    }

    public static async Task<LinkAllowResult> ToggleAllowedLinkDomainAsync(DbService db, ulong guildId, string domain)
    {
        await using var uow = db.GetDbContext();
        var removed = await uow.GetTable<AllowedLinkDomain>()
            .Where(x => x.GuildId == guildId && x.Domain == domain)
            .DeleteAsync();

        if (removed > 0)
            return LinkAllowResult.Removed;

        // The count and the insert are one statement, so parallel commands cannot pass the cap.
        await using var lctx = uow.CreateLinqToDBContext();
        var domains = lctx.GetTable<AllowedLinkDomain>();
        try
        {
            var inserted = await lctx.SelectQuery(() => 1)
                .Where(_ => domains.Count(d => d.GuildId == guildId) < MAX_ALLOWED_DOMAINS)
                .InsertAsync(domains,
                    _ => new AllowedLinkDomain
                    {
                        GuildId = guildId,
                        Domain = domain,
                    });

            return inserted > 0 ? LinkAllowResult.Added : LinkAllowResult.LimitReached;
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
            when (ex.SqliteExtendedErrorCode == NadekoBot.Db.DbExtensions.SQLITE_CONSTRAINT_UNIQUE)
        {
            // another command added the same domain a moment ago
            return LinkAllowResult.Added;
        }
    }

    public async Task<IReadOnlyList<string>> GetAllowedLinkDomainsAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<AllowedLinkDomain>()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.Domain)
            .Select(x => x.Domain)
            .ToListAsyncLinqToDB();
    }

    public async Task ClearAllowedLinkDomainsAsync(ulong guildId)
    {
        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<AllowedLinkDomain>()
                .Where(x => x.GuildId == guildId)
                .DeleteAsync();
        }

        await ReloadAllowedDomainsInternalAsync(guildId);
    }

    // A load which starts later sees every write before it, so only a newer load may replace the set.
    private async Task ReloadAllowedDomainsInternalAsync(ulong guildId)
    {
        var version = Interlocked.Increment(ref _allowedDomainsVersion);

        await using var uow = _db.GetDbContext();
        var domains = await uow.GetTable<AllowedLinkDomain>()
            .Where(x => x.GuildId == guildId)
            .Select(x => x.Domain)
            .ToListAsyncLinqToDB();

        StoreAllowedDomainsInternal(guildId, new(version, domains.ToFrozenSet(StringComparer.OrdinalIgnoreCase)));
    }

    private void StoreAllowedDomainsInternal(ulong guildId, AllowedDomainSet next)
        => _allowedDomains.AddOrUpdate(guildId, next, (_, old) => old.Version > next.Version ? old : next);

    private sealed record AllowedDomainSet(long Version, FrozenSet<string> Domains);

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
            FilterInvitesEnabled = conf?.FilterInvites ?? InviteFilteringServers.Contains(guildId),
            FilterLinksEnabled = conf?.FilterLinks ?? LinkFilteringServers.Contains(guildId),
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
            LinkFilteringServers.TryRemove(guildId);
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

        InviteFilteringServers.TryRemove(guildId);
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

        WordFilteringServers.TryRemove(guildId);
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
        word = word?.Trim();

        await using var uow = _db.GetDbContext();
        var fc = uow.FilterConfigForId(guildId, set => set.Include(x => x.FilteredWords));
        var sfw = _serverFilteredWords.GetOrAdd(guildId, static _ => new());
        if (sfw.Add(word))
        {
            fc.FilteredWords.Add(new FilteredWord()
            {
                Word = word
            });

            await uow.SaveChangesAsync();
            return true;
        }

        sfw.Remove(word);
        fc.FilteredWords.RemoveWhere(x => string.Equals(x.Word, word, StringComparison.InvariantCultureIgnoreCase));
        await uow.SaveChangesAsync();

        return false;
    }

    private sealed class FilteredWordSet
    {
        private readonly Lock _lock = new();
        private HashSet<string> _writeSet = new(StringComparer.InvariantCultureIgnoreCase);
        private volatile HashSet<string> _readSnapshot = new(StringComparer.InvariantCultureIgnoreCase);

        public HashSet<string> Snapshot => _readSnapshot;

        public bool Add(string word)
        {
            lock (_lock)
            {
                if (!_writeSet.Add(word)) return false;
                _readSnapshot = new HashSet<string>(_writeSet, StringComparer.InvariantCultureIgnoreCase);
                return true;
            }
        }

        public bool Remove(string word)
        {
            lock (_lock)
            {
                if (!_writeSet.Remove(word)) return false;
                _readSnapshot = new HashSet<string>(_writeSet, StringComparer.InvariantCultureIgnoreCase);
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _writeSet = new(StringComparer.InvariantCultureIgnoreCase);
                _readSnapshot = new(StringComparer.InvariantCultureIgnoreCase);
            }
        }

        public void Bulk(IEnumerable<string> words)
        {
            lock (_lock)
            {
                foreach (var w in words) _writeSet.Add(w);
                _readSnapshot = new HashSet<string>(_writeSet, StringComparer.InvariantCultureIgnoreCase);
            }
        }
    }
}
