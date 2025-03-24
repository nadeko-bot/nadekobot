using DryIoc.ImTools;
using NadekoBot.Modules.Utility.LinkFixer;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public class LinkFixerCommands : NadekoModule<LinkFixerService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task LinkFix(string oldDomain, string? newDomain = null)
        {
            if (string.IsNullOrWhiteSpace(newDomain))
            {
                var rmSuccess = await _service.RemoveLinkFixAsync(ctx.Guild.Id, oldDomain);

                if (rmSuccess)
                    await Response().Confirm(strs.linkfix_removed(Format.Bold(oldDomain))).SendAsync();
                else
                    await Response().Error(strs.linkfix_not_found(Format.Bold(oldDomain))).SendAsync();

                return;
            }

            oldDomain = CleanDomain(oldDomain);
            newDomain = newDomain.Trim();

            if (string.IsNullOrWhiteSpace(oldDomain) || string.IsNullOrWhiteSpace(newDomain))
            {
                await Response().Error(strs.linkfix_invalid_domains).SendAsync();
                return;
            }

            var success = await _service.AddLinkFixAsync(ctx.Guild.Id, oldDomain, newDomain);
            if (success)
                await Response().Confirm(strs.linkfix_added(Format.Bold(oldDomain), Format.Bold(newDomain))).SendAsync();
            else
                await Response().Error(strs.linkfix_already_exists(Format.Bold(oldDomain))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task LinkFixList()
        {
            var linkFixes = _service.GetLinkFixes(ctx.Guild.Id);
            if (linkFixes.Count == 0)
            {
                await Response().Confirm(strs.linkfix_list_none).SendAsync();
                return;
            }

            var items = linkFixes.Select(x => $"{Format.Bold(x.Key)} -> {Format.Bold(x.Value)}").ToList();

            await Response()
                .Paginated()
                .Items(items)
                .PageSize(10)
                .Page((items, _) =>
                {
                    var eb = CreateEmbed()
                        .WithTitle(GetText(strs.linkfix_list_title))
                        .WithDescription(string.Join('\n', items))
                        .WithOkColor();

                    return eb;
                })
                .SendAsync();
        }

        /// <summary>
        /// Removes protocol and www. from a domain
        /// </summary>
        /// <param name="domain">The domain to clean</param>
        private static string CleanDomain(string domain)
        {
            // Remove protocol if present
            if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                domain = domain[7..];
            else if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                domain = domain[8..];

            // Remove www. if present
            if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                domain = domain[4..];

            // Remove any path or query string
            var pathIndex = domain.IndexOf('/');
            if (pathIndex > 0)
                domain = domain[..pathIndex];

            if (domain.Split('.').Length != 2)
                return string.Empty;

            return domain.ToLowerInvariant();
        }
    }
}