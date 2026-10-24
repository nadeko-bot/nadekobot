using System.Text;
using System.Text.RegularExpressions;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.LinkFixer;

/// <summary>
/// Service for managing link fixing functionality
/// </summary>
public partial class LinkFixerService(DbService db, ShardData shardData) : IReadyExecutor, IExecNoCommand, INService
{
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, string>> _guildLinkFixes = new();

    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();
        var linkFixes = await uow.GetTable<LinkFix>()
            .Where(Queries.GuildOnShard<LinkFix>(x => x.GuildId, shardData.TotalShards, shardData.ShardId))
            .ToListAsyncLinqToDB();

        foreach (var fix in linkFixes)
        {
            var guildDict = _guildLinkFixes.GetOrAdd(fix.GuildId, static _ => new(StringComparer.InvariantCultureIgnoreCase));
            guildDict.TryAdd(fix.OldDomain.ToLowerInvariant(), fix.NewDomain);
        }
    }

    public const int MAX_FIXED_LINKS = 5;

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (guild is not SocketGuild sg)
            return;

        if (!_guildLinkFixes.TryGetValue(guild.Id, out var guildDict) || guildDict.IsEmpty)
            return;

        // a reply needs read message history too, a 403 here would repeat on every message
        if (msg.Channel is not IGuildChannel channel)
            return;

        var perms = sg.CurrentUser.GetPermissions(channel);
        var canSend = channel is IThreadChannel ? perms.SendMessagesInThreads : perms.SendMessages;
        if (!canSend || !perms.ReadMessageHistory)
            return;

        var reply = BuildReply(msg.Content, guildDict);
        if (reply is null)
            return;

        await msg.ReplyAsync(reply, allowedMentions: AllowedMentions.None);
    }

    public static string? BuildReply(string? content, IReadOnlyDictionary<string, string> fixes)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        StringBuilder? sb = null;
        var found = 0;
        foreach (var range in content.AsSpan().SplitAny(" \r\n\t"))
        {
            var (start, length) = range.GetOffsetAndLength(content.Length);
            if (length == 0)
                continue;

            var match = FullUrlRegex().Match(content, start, length);
            if (!match.Success)
                continue;

            if (!fixes.TryGetValue(match.Groups["domain"].Value, out var newDomain))
                continue;

            var prefix = match.Groups["prefix"];
            var suffix = match.Groups["suffix"];
            var lineLength = prefix.Length + newDomain.Length + suffix.Length;
            if ((sb?.Length ?? 0) + (found > 0 ? 1 : 0) + lineLength > DiscordConfig.MaxMessageSize)
                continue;

            sb ??= new();
            if (found > 0)
                sb.Append('\n');

            sb.Append(prefix.ValueSpan)
              .Append(newDomain)
              .Append(suffix.ValueSpan);

            if (++found == MAX_FIXED_LINKS)
                break;
        }

        return sb?.ToString();
    }

    [GeneratedRegex(@"(?<prefix>https?://)(?:www.)?(?<domain>[a-zA-Z0-9\-\.]+)(?<suffix>/.*)?")]
    private static partial Regex FullUrlRegex();
    
    [GeneratedRegex(@"(?<prefix>https?://)?(?:ww[w\d].)?(?<domain>[a-zA-Z0-9\-\.]+)(?<suffix>/.*)?")]
    public partial Regex PartialUrlRegex();

    /// <summary>
    /// Adds a new link fix for a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="oldDomain">Domain to be replaced</param>
    /// <param name="newDomain">Domain to replace with</param>
    /// <returns>True if successfully added, false if already exists</returns>
    public async Task<bool> AddLinkFixAsync(ulong guildId, string oldDomain, string newDomain)
    {
        oldDomain = oldDomain.ToLowerInvariant();

        var guildDict = _guildLinkFixes.GetOrAdd(guildId,
            static _ => new ConcurrentDictionary<string, string>(StringComparer.InvariantCultureIgnoreCase));
        guildDict[oldDomain] = newDomain;

        await using var uow = db.GetDbContext();
        await uow.GetTable<LinkFix>()
            .InsertOrUpdateAsync(() => new LinkFix
                {
                    GuildId = guildId,
                    OldDomain = oldDomain,
                    NewDomain = newDomain
                },
                old => new LinkFix
                {
                    NewDomain = newDomain
                },
                () => new LinkFix
                {
                    GuildId = guildId,
                    OldDomain = oldDomain,
                });

        return true;
    }

    /// <summary>
    /// Removes a link fix from a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="oldDomain">Domain to remove from fixes</param>
    /// <returns>True if successfully removed, false if not found</returns>
    public async Task<bool> RemoveLinkFixAsync(ulong guildId, string oldDomain)
    {
        oldDomain = oldDomain.ToLowerInvariant();

        if (!_guildLinkFixes.TryGetValue(guildId, out var guildDict) || !guildDict.TryRemove(oldDomain, out _))
            return false;

        await using var uow = db.GetDbContext();
        await uow.GetTable<LinkFix>()
            .DeleteAsync(lf => lf.GuildId == guildId && lf.OldDomain == oldDomain);

        return true;
    }

    /// <summary>
    /// Gets all link fixes for a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <returns>Dictionary of old domains to new domains</returns>
    public IReadOnlyDictionary<string, string> GetLinkFixes(ulong guildId)
    {
        if (_guildLinkFixes.TryGetValue(guildId, out var guildDict))
            return guildDict;

        return new Dictionary<string, string>();
    }
}