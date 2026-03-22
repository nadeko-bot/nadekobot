using System.Net.Http.Json;
using System.Text;
using NadekoBot.Common.ModuleBehaviors;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Owner.Self;

public sealed class GithubReleaseModel
{
    [JsonPropertyName("tag_name")]
    public required string TagName { get; init; }
}

public sealed class CheckForUpdatesService(
    BotConfigService bcs,
    IBotCredsProvider bcp,
    IHttpClientFactory httpFactory,
    DiscordSocketClient client,
    IMessageSenderService sender)
    : INService, IReadyExecutor
{
    private const string RELEASES_URL = "https://api.github.com/repos/nadeko-bot/nadekobot/releases";

    public async Task OnReadyAsync()
    {
        if (client.ShardId != 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync())
        {
            var conf = bcs.Data;

            if (!conf.CheckForUpdates)
                continue;

            try
            {
                using var http = httpFactory.CreateClient();
                http.DefaultRequestHeaders.Clear();
                http.DefaultRequestHeaders.Add("User-Agent", "nadekobot_" + client.CurrentUser.Id.ToString()[^5..]);
                var githubRelease = (await http.GetFromJsonAsync<GithubReleaseModel[]>(RELEASES_URL))
                    ?.FirstOrDefault();

                if (githubRelease?.TagName is null)
                    continue;

                var latest = githubRelease.TagName;
                var latestVersion = Version.Parse(latest);
                var lastKnownVersion = GetLastKnownVersion();

                if (lastKnownVersion is null)
                {
                    UpdateLastKnownVersion(latestVersion);
                    continue;
                }

                if (latestVersion > lastKnownVersion)
                {
                    UpdateLastKnownVersion(latestVersion);

                    // pull changelog
                    var changelog = await http.GetStringAsync("https://raw.githubusercontent.com/nadeko-bot/nadekobot/refs/heads/v6/CHANGELOG.md");

                    var thisVersionChangelog = GetVersionChangelog(latestVersion, changelog);

                    if (string.IsNullOrWhiteSpace(thisVersionChangelog))
                    {
                        Log.Warning("New version {BotVersion} was found but changelog is unavailable",
                            thisVersionChangelog);
                        continue;
                    }

                    var creds = bcp.GetCreds();
                    await creds.OwnerIds
                               .Select(async x =>
                               {
                                   var user = await client.GetUserAsync(x);
                                   if (user is null)
                                       return;

                                   var eb = sender.CreateEmbed()
                                                   .WithOkColor()
                                                   .WithAuthor($"NadekoBot v{latest} Released!")
                                                   .WithTitle("Changelog")
                                                   .WithUrl("https://github.com/nadeko-bot/nadekobot/blob/refs/heads/v6/CHANGELOG.md")
                                                   .WithDescription(thisVersionChangelog.TrimTo(4096))
                                                   .WithFooter(
                                                       "You may disable these messages by typing '.conf bot checkforupdates false'");

                                   await sender.Response(user).Embed(eb).SendAsync();
                               })
                               .WhenAll();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while checking for new bot release: {ErrorMessage}", ex.Message);
            }
        }
    }

    private string? GetVersionChangelog(Version latestVersion, string changelog)
    {
        var clSpan = changelog.AsSpan();

        var sb = new StringBuilder();
        var started = false;
        foreach (var line in clSpan.EnumerateLines())
        {
            if (started)
            {
                if (line.StartsWith("## ["))
                    break;

                sb.AppendLine(line.ToString());
                continue;
            }

            if (line.StartsWith($"## [{latestVersion.ToString()}]"))
            {
                started = true;
                continue;
            }
        }

        return sb.ToString().Trim();
    }

    private static readonly string LAST_KNOWN_VERSION_PATH =
        Path.Combine(Directory.GetCurrentDirectory(), "data/last_known_version.txt");

    private static Version? GetLastKnownVersion()
    {
        if (!File.Exists(LAST_KNOWN_VERSION_PATH))
            return null;

        return Version.TryParse(File.ReadAllText(LAST_KNOWN_VERSION_PATH), out var ver)
            ? ver
            : null;
    }

    private static void UpdateLastKnownVersion(Version version)
    {
        File.WriteAllText(LAST_KNOWN_VERSION_PATH, version.ToString());
    }
}