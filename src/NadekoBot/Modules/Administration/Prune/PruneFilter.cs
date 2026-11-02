namespace NadekoBot.Modules.Administration.Services;

public sealed record PruneFilter(
    bool Bots,
    bool Humans,
    bool Links,
    bool Files,
    bool Embeds,
    bool Invites,
    bool Pings,
    string? Contains)
{
    public bool IsEmpty
        => !Bots && !Humans && !Links && !Files && !Embeds && !Invites && !Pings && string.IsNullOrEmpty(Contains);

    public bool Matches(IMessage msg)
    {
        if (Bots && !msg.Author.IsBot)
            return false;

        if (Humans && msg.Author.IsBot)
            return false;

        var content = msg.Content ?? string.Empty;

        if (Links
            && !content.Contains("http://", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        if (Files && msg.Attachments.Count == 0)
            return false;

        if (Embeds && msg.Embeds.Count == 0)
            return false;

        if (Invites && !content.IsDiscordInvite())
            return false;

        if (Pings && msg.MentionedUserIds.Count == 0 && msg.MentionedRoleIds.Count == 0 && !msg.MentionedEveryone)
            return false;

        if (!string.IsNullOrEmpty(Contains) && !content.Contains(Contains, StringComparison.InvariantCultureIgnoreCase))
            return false;

        return true;
    }
}
