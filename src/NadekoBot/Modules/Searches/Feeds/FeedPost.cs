namespace NadekoBot.Modules.Searches.Services;

public readonly record struct FeedPost(string? Link, string Title, string? Description, string? ImageUrl);

public readonly record struct FeedMessage(SmartText Text, bool WithDefaultEmbed);
