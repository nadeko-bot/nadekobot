using System.Text.RegularExpressions;

namespace NadekoBot;

// Decides what a message built from a saved template may ping. Only mentions which the author wrote into the
// message content count; a placeholder value, a %rng% split or a JSON escape can show "@everyone" but not ping.
public static partial class TemplateMentions
{
    // Discord accepts at most 100 role ids in allowed_mentions.
    private const int MAX_ROLE_IDS = 100;

    [GeneratedRegex(@"<@&(\d{1,20})>")]
    private static partial Regex RoleMentionRegex();

    public static AllowedMentions FromTemplate(SmartText? template)
        => FromContent(template switch
        {
            SmartPlainText plain => plain.Text,
            SmartEmbedTextArray arr => arr.Content,
            _ => null
        });

    public static AllowedMentions FromTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return new(AllowedMentionTypes.Users);

        // Only an object can be rich text; skip the parse (and its exception) for plain text.
        return template.AsSpan().TrimStart().StartsWith('{')
            ? FromTemplate(SmartText.CreateFrom(template))
            : FromContent(template);
    }

    public static AllowedMentions FromContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return new(AllowedMentionTypes.Users);

        var types = AllowedMentionTypes.Users;
        if (content.Contains("@everyone", StringComparison.InvariantCultureIgnoreCase)
            || content.Contains("@here", StringComparison.InvariantCultureIgnoreCase))
            types |= AllowedMentionTypes.Everyone;

        var mentions = new AllowedMentions(types);
        if (!content.Contains("<@&", StringComparison.Ordinal))
            return mentions;

        var roleIds = new List<ulong>();
        foreach (var match in RoleMentionRegex().EnumerateMatches(content))
        {
            if (roleIds.Count == MAX_ROLE_IDS)
                break;

            if (ulong.TryParse(content.AsSpan(match.Index + 3, match.Length - 4), out var id) && !roleIds.Contains(id))
                roleIds.Add(id);
        }

        if (roleIds.Count > 0)
            mentions.RoleIds = roleIds;

        return mentions;
    }
}
