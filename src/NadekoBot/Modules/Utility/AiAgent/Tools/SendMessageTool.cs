using System.Text.Json;
using System.Text.RegularExpressions;
using Discord;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Sends a text message (optionally with an embed) to a specified channel.
/// Accepts channel ID, mention format, or #channel-name.
/// Checks that the invoking user has SendMessages permission in the target channel.
/// </summary>
public sealed partial class SendMessageTool : IAiTool, INService
{
    public string Name => "send_message";

    public string Description =>
        "Send a message to a Discord channel. " +
        "You can specify the channel by ID, mention (like <#123456>), or name (like #general). " +
        "The message will appear as sent by the bot, not the user. " +
        "Messages longer than 2000 characters will be rejected. " +
        "Optionally include an embed for rich formatting.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "channel": {
                    "type": "string",
                    "description": "The target channel - can be an ID, a mention like <#123456>, or a name like #general"
                },
                "text": {
                    "type": "string",
                    "description": "The text content to send (can be empty if embed is provided)"
                },
                "embed": {
                    "type": "object",
                    "description": "Optional rich embed to attach to the message",
                    "properties": {
                        "title": {
                            "type": "string",
                            "description": "Embed title (max 256 chars). Note: mentions and custom emoji don't render in titles."
                        },
                        "description": {
                            "type": "string",
                            "description": "Embed description/body text (max 4096 chars). Supports mentions, emoji, and markdown."
                        },
                        "color": {
                            "type": "string",
                            "description": "Hex color code like #FF0000 (red), #00FF00 (green), #0000FF (blue), or a name (red, green, blue, yellow, orange, purple, teal, gold, magenta)"
                        },
                        "fields": {
                            "type": "array",
                            "description": "List of embed fields",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string", "description": "Field name (max 256 chars)" },
                                    "value": { "type": "string", "description": "Field value (max 1024 chars)" },
                                    "inline": { "type": "boolean", "description": "Show field inline (default false)" }
                                },
                                "required": ["name", "value"]
                            }
                        },
                        "footer": {
                            "type": "string",
                            "description": "Footer text (max 2048 chars). Note: mentions and custom emoji don't render in footers."
                        }
                    }
                }
            },
            "required": ["channel"]
        }
        """).RootElement.Clone();

    private static readonly Dictionary<string, Color> _namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = Color.Red,
        ["green"] = Color.Green,
        ["blue"] = Color.Blue,
        ["yellow"] = new Color(255, 255, 0),
        ["orange"] = Color.Orange,
        ["purple"] = Color.Purple,
        ["teal"] = Color.Teal,
        ["gold"] = Color.Gold,
        ["magenta"] = Color.Magenta,
    };

    [GeneratedRegex(@"<#(\d+)>")]
    private static partial Regex ChannelMentionRegex();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("channel", out var channelEl))
            return "Error: channel is required.";

        var channelStr = channelEl.GetString();
        if (string.IsNullOrWhiteSpace(channelStr))
            return "Error: channel cannot be empty.";

        arguments.TryGetProperty("text", out var textEl);
        var text = textEl.ValueKind == JsonValueKind.String ? textEl.GetString() : null;

        var hasEmbed = arguments.TryGetProperty("embed", out var embedEl)
                       && embedEl.ValueKind == JsonValueKind.Object;

        if (string.IsNullOrWhiteSpace(text) && !hasEmbed)
            return "Error: Either text or embed (or both) must be provided.";

        var channel = await ResolveChannelAsync(context, channelStr);
        if (channel is null)
            return "Error: Channel not found. Make sure the channel exists and is a text channel.";

        var userPerms = context.User.GetPermissions(channel);
        if (!userPerms.SendMessages)
            return "Error: You don't have permission to send messages in that channel.";

        if (text is not null && text.Length > 2000)
            return "Error: Message exceeds Discord's 2000 character limit. Shorten the text or move content into an embed.";

        Embed? embed = null;
        if (hasEmbed)
        {
            if (hasEmbed && !userPerms.EmbedLinks)
                return "Error: You don't have permission to embed links in that channel.";

            var (builtEmbed, buildError) = TryBuildEmbed(embedEl);
            if (buildError is not null)
                return buildError;

            if (builtEmbed is not null
                && string.IsNullOrWhiteSpace(builtEmbed.Description)
                && string.IsNullOrWhiteSpace(builtEmbed.Title)
                && builtEmbed.Fields.Length == 0)
                return "Error: Embed must have at least a title, description, or fields.";

            embed = builtEmbed;
        }

        await channel.SendMessageAsync(text ?? "", embed: embed);
        return $"Message sent to #{channel.Name} successfully.";
    }

    /// <summary>
    /// Attempts to build a Discord embed from the JSON element. Returns the embed or an error string.
    /// </summary>
    private static (Embed?, string?) TryBuildEmbed(JsonElement el)
    {
        var eb = new EmbedBuilder();

        if (el.TryGetProperty("title", out var titleEl) && titleEl.GetString() is { } title)
        {
            if (title.Length > 256)
                return (null, "Error: Embed title must be 256 characters or less.");
            eb.WithTitle(title);
        }

        if (el.TryGetProperty("description", out var descEl) && descEl.GetString() is { } desc)
        {
            if (desc.Length > 4096)
                return (null, "Error: Embed description must be 4096 characters or less.");
            eb.WithDescription(desc);
        }

        if (el.TryGetProperty("color", out var colorEl))
        {
            if (colorEl.ValueKind == JsonValueKind.Number && colorEl.TryGetUInt32(out var numColor))
                eb.WithColor(new Color(numColor));
            else if (colorEl.ValueKind == JsonValueKind.String && colorEl.GetString() is { } colorStr)
            {
                if (_namedColors.TryGetValue(colorStr, out var namedColor))
                    eb.WithColor(namedColor);
                else if (colorStr.StartsWith('#') && uint.TryParse(colorStr[1..],
                             System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                    eb.WithColor(new Color(hexVal));
            }
        }

        if (el.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var fieldEl in fieldsEl.EnumerateArray())
            {
                var name = fieldEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var value = fieldEl.TryGetProperty("value", out var valueEl) ? valueEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (name.Length > 256)
                    return (null, "Error: Embed field name must be 256 characters or less.");
                if (value.Length > 1024)
                    return (null, "Error: Embed field value must be 1024 characters or less.");

                var inline = fieldEl.TryGetProperty("inline", out var inlineEl)
                             && inlineEl.ValueKind == JsonValueKind.True;

                eb.AddField(name, value, inline);
            }

            if (eb.Fields.Count > 25)
                return (null, "Error: Embeds can have at most 25 fields.");
        }

        if (el.TryGetProperty("footer", out var footerEl) && footerEl.GetString() is { } footer)
        {
            if (footer.Length > 2048)
                return (null, "Error: Embed footer must be 2048 characters or less.");
            eb.WithFooter(footer);
        }

        return (eb.Build(), null);
    }

    private static async Task<ITextChannel?> ResolveChannelAsync(AiToolContext context, string input)
    {
        input = input.Trim();

        var mentionMatch = ChannelMentionRegex().Match(input);
        if (mentionMatch.Success && ulong.TryParse(mentionMatch.Groups[1].Value, out var mentionId))
            return await context.Guild.GetTextChannelAsync(mentionId);

        if (ulong.TryParse(input, out var rawId))
            return await context.Guild.GetTextChannelAsync(rawId);

        var name = input.TrimStart('#').ToLowerInvariant();
        var channels = await context.Guild.GetTextChannelsAsync();
        return channels.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
