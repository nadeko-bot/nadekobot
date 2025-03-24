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
public partial class LinkFixerService(DbService db) : IReadyExecutor, IExecNoCommand, INService
{
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, string>> _guildLinkFixes = new();

    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();
        var linkFixes = await uow.GetTable<LinkFix>()
            .AsNoTracking()
            .ToListAsyncLinqToDB();

        foreach (var fix in linkFixes)
        {
            var guildDict = _guildLinkFixes.GetOrAdd(fix.GuildId, _ => new(StringComparer.InvariantCultureIgnoreCase));
            guildDict.TryAdd(fix.OldDomain.ToLowerInvariant(), fix.NewDomain);
        }
    }

    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        if (guild is null)
            return;

        var guildId = guild.Id;
        if (!_guildLinkFixes.TryGetValue(guildId, out var guildDict))
            return;

        var content = msg.Content;
        if (string.IsNullOrWhiteSpace(content))
            return;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var match = UrlRegex().Match(word);
            if (!match.Success)
                continue;

            var domain = match.Groups["domain"].Value;
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            if (!guildDict.TryGetValue(domain, out var newDomain))
                continue;

            var newUrl = match.Groups["prefix"].Value + newDomain + match.Groups["suffix"].Value;
            await msg.ReplyAsync(newUrl, allowedMentions: AllowedMentions.None);
        }
    }

    [GeneratedRegex("(?<prefix>https?://(?:www\\.)?)(?<domain>[^/]+)(?<suffix>.*)")]
    private partial Regex UrlRegex();

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

        var guildDict = _guildLinkFixes.GetOrAdd(guildId, _ => new ConcurrentDictionary<string, string>());
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