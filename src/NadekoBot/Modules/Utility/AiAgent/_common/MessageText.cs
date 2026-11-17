using System.Text;

namespace NadekoBot.Modules.Utility.AiAgent;

public static class MessageText
{
    // Command output replies to the member's request, so that reply line would only repeat the request.
    public static string Read(IMessage msg, bool asCommandOutput = false)
        => ReadInternal(msg, asCommandOutput, out _, out _);

    // Also gives, in quoted, everything but the author's own text, for the guard.
    public static string Read(IMessage msg, out string quoted)
    {
        var text = ReadInternal(msg, false, out var contentStart, out var contentEnd);
        if (contentStart == contentEnd)
        {
            quoted = text;
            return text;
        }

        // The text line can be the last one, which lost its line end.
        var before = text.AsSpan(0, contentStart);
        var after = text.AsSpan(Math.Min(contentEnd, text.Length));
        quoted = after.IsEmpty ? before.TrimEnd('\n').ToString() : string.Concat(before, after);
        return text;
    }

    private static string ReadInternal(IMessage msg, bool asCommandOutput, out int contentStart, out int contentEnd)
    {
        var sb = new StringBuilder();

        if (!asCommandOutput && msg is IUserMessage userMsg && userMsg.ReferencedMessage is { } reply)
        {
            var replyAuthor = reply.Author?.Username ?? "?";
            var replyExcerpt = reply.Content;
            if (!string.IsNullOrEmpty(replyExcerpt))
                sb.Append("reply_to: ").Append(replyAuthor).Append(": ").Append(replyExcerpt).Append('\n');
            else
                sb.Append("reply_to: ").Append(replyAuthor).Append('\n');
        }

        contentStart = sb.Length;
        if (!string.IsNullOrWhiteSpace(msg.Content))
            sb.Append("text: ").Append(msg.Content).Append('\n');

        contentEnd = sb.Length;
        var layout = LayoutText.Read(msg.Components);
        if (layout.Length > 0)
            sb.Append("text: ").Append(layout).Append('\n');

        foreach (var embed in msg.Embeds)
            AppendEmbedInternal(sb, embed);

        foreach (var att in msg.Attachments)
        {
            sb.Append("attachment: ").Append(att.Filename);
            if (!string.IsNullOrWhiteSpace(att.ContentType))
                sb.Append(" (").Append(att.ContentType).Append(')');
            if (!asCommandOutput && att.Size > 0)
                sb.Append(' ').Append(att.Size).Append('B');
            if (!asCommandOutput && !string.IsNullOrWhiteSpace(att.Url))
                sb.Append(' ').Append(att.Url);
            sb.Append('\n');
        }

        foreach (var sticker in msg.Stickers)
            sb.Append("sticker: ").Append(sticker.Name).Append('\n');

        if (sb.Length > 0 && sb[^1] == '\n')
            sb.Length--;

        return sb.ToString();
    }

    private static void AppendEmbedInternal(StringBuilder sb, IEmbed embed)
    {
        sb.Append("embed:\n");

        if (embed.Author is { Name: { } authorName } && !string.IsNullOrWhiteSpace(authorName))
            sb.Append("  author: ").Append(authorName).Append('\n');
        if (!string.IsNullOrWhiteSpace(embed.Title))
            sb.Append("  title: ").Append(embed.Title).Append('\n');
        if (!string.IsNullOrWhiteSpace(embed.Url))
            sb.Append("  link: ").Append(embed.Url).Append('\n');
        if (!string.IsNullOrWhiteSpace(embed.Description))
            sb.Append("  description: ").Append(embed.Description).Append('\n');

        foreach (var field in embed.Fields)
            sb.Append("  field: ").Append(field.Name).Append(" = ").Append(field.Value).Append('\n');

        if (embed.Image is { Url: { } imageUrl } && !string.IsNullOrWhiteSpace(imageUrl))
            sb.Append("  image: ").Append(imageUrl).Append('\n');
        if (embed.Thumbnail is { Url: { } thumbUrl } && !string.IsNullOrWhiteSpace(thumbUrl))
            sb.Append("  thumbnail: ").Append(thumbUrl).Append('\n');

        if (embed.Footer is { Text: { } footerText } && !string.IsNullOrWhiteSpace(footerText))
            sb.Append("  footer: ").Append(footerText).Append('\n');

        if (embed.Timestamp is { } embedTs)
            sb.Append("  timestamp: ").Append(embedTs.ToUnixTimeSeconds()).Append('\n');
    }
}
