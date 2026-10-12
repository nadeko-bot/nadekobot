#nullable enable
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Games;
using NadekoBot.Modules.Patronage;
using NadekoBot.Modules.Searches;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ConfigPolicyTests
{
    private static BotConfigService Bot()
        => new(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));

    private static PatronageConfig Patron(bool managed)
        => new(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(managed));

    [Test]
    public void Props_ReportTheirTypeAndLimitsInTheDashboardFields()
    {
        var bot = Bot();
        var gambling = new GamblingConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));

        Assert.That(bot.GetPropInfo("PREFIX"),
            Is.EqualTo(new ConfigPropInfo(ConfigPropType.Text, MaxLength: BotConfigService.MAX_PREFIX_LENGTH)));
        Assert.That(bot.GetPropInfo("color.ok")!.Type, Is.EqualTo(ConfigPropType.Color));
        Assert.That(bot.GetPropInfo("checkforupdates")!.Type, Is.EqualTo(ConfigPropType.Switch));
        Assert.That(bot.GetPropInfo("locale")!.Type, Is.EqualTo(ConfigPropType.Language));
        Assert.That(bot.GetPropInfo("timezone")!.Type, Is.EqualTo(ConfigPropType.Timezone));

        var console = bot.GetPropInfo("console.type")!;
        Assert.That((console.Type, console.Options), Is.EqualTo((ConfigPropType.Choice, new[] { "Normal", "Simple", "None" })));

        var chance = gambling.GetPropInfo("gen.chance")!;
        Assert.That((chance.Type, chance.Min, chance.Max), Is.EqualTo((ConfigPropType.Decimal, (double?)0, (double?)1)));

        var minBet = gambling.GetPropInfo("minbet")!;
        Assert.That((minBet.Type, minBet.Min, minBet.Max), Is.EqualTo((ConfigPropType.Number, (double?)0, (double?)null)));

        Assert.That(bot.GetPropInfo("missing"), Is.Null);

        var registry = new ConfigRegistry([bot], new BotPolicy(false));
        var config = ((JsonHttpResult<ConfigDto>)ApiEndpoints.GetConfig("bot", registry)).Value!;
        var prefixDto = config.Props.Single(static x => x.Prop == "prefix");
        var consoleDto = config.Props.Single(static x => x.Prop == "console.type");

        using var prefixJson = JsonDocument.Parse(JsonSerializer.Serialize(prefixDto, ApiJsonContext.Default.ConfigPropDto));
        Assert.That(prefixJson.RootElement.GetProperty("type").GetString(), Is.EqualTo("text"));
        Assert.That(prefixJson.RootElement.GetProperty("maxLength").GetInt32(), Is.EqualTo(BotConfigService.MAX_PREFIX_LENGTH));

        using var consoleJson = JsonDocument.Parse(JsonSerializer.Serialize(consoleDto, ApiJsonContext.Default.ConfigPropDto));
        Assert.That(consoleJson.RootElement.GetProperty("type").GetString(), Is.EqualTo("choice"));
        Assert.That(consoleJson.RootElement.GetProperty("options").GetArrayLength(), Is.GreaterThan(1));
    }

    [Test]
    public void SetSetting_RefusesValuesOutsideTheLimits()
    {
        var bot = Bot();
        var fish = new FishConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        var searches = new SearchesConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        var waifu = WaifuTestHelper.CreateConfigService();

        Assert.That(fish.SetSetting("chance.fish", "-1"), Is.False);
        Assert.That(fish.SetSetting("chance.trash", (FishConfigService.MAX_CHANCE_WEIGHT + 1).ToString()), Is.False);
        Assert.That(fish.SetSetting("currency.min", "0"), Is.False);
        Assert.That(searches.SetSetting("followedStreams.maxCount", "-2"), Is.False);
        Assert.That(searches.SetSetting("feeds.maxCount", (SearchesConfigService.MAX_PER_SERVER + 1).ToString()), Is.False);
        Assert.That(waifu.SetSetting("decay", "101"), Is.False);
        Assert.That(bot.SetSetting("prefix", new string('!', BotConfigService.MAX_PREFIX_LENGTH + 1)), Is.False);
        Assert.That(bot.SetSetting("prefix", " "), Is.False);
        Assert.That(bot.SetSetting("help.text", new string('a', BotConfigService.MAX_HELP_TEXT_LENGTH + 1)), Is.False);

        Assert.That(waifu.SetSetting("decay", "100"), Is.True);
        Assert.That(waifu.GetSetting("decay"), Is.EqualTo("100"));
    }

    [Test]
    public void Policy_LocksHostSettingsOnlyOnManagedBots()
    {
        var managed = new BotPolicy(true);
        var selfHosted = new BotPolicy(false);

        string[][] locked =
        [
            ["bot", "checkforupdates"], ["BOT", "Console.Type"], ["patron", "enabled"], ["music", "maxcachesizegb"],
            ["searches", "WEBENGINE"], ["searches", "imgEngine"],
            ["searches", "ytProvider"]
        ];

        foreach (var pair in locked)
        {
            Assert.That(managed.IsLocked(pair[0], pair[1]), Is.True, string.Join('.', pair));
            Assert.That(selfHosted.IsLocked(pair[0], pair[1]), Is.False, string.Join('.', pair));
        }

        Assert.That(managed.IsLocked("bot", "prefix"), Is.False);
        Assert.That(managed.IsLocked("aiagent", "enabled"), Is.False);
        Assert.That(managed.IsLocked("searches", "feeds.maxCount"), Is.False);

        Assert.That(SearchEngineChoice.Img(new SearchesConfig { ImgSearchEngine = ImgSearchEngine.Google }, managed),
            Is.EqualTo(ImgSearchEngine.DuckDuckGo));
        Assert.That(SearchEngineChoice.Img(new SearchesConfig { ImgSearchEngine = ImgSearchEngine.Google }, selfHosted),
            Is.EqualTo(ImgSearchEngine.Google));
        Assert.That(Patron(true).IsEnabled, Is.False);
    }

    [Test]
    public void ManagedRegistry_HidesLockedSettingsAndRefusesToSetThem()
    {
        var bot = Bot();
        var registry = new ConfigRegistry([bot, Patron(true)], new BotPolicy(true));

        Assert.That(registry.Names, Is.EqualTo(new[] { "bot" }));

        var config = ((JsonHttpResult<ConfigDto>)ApiEndpoints.GetConfig("bot", registry)).Value!;
        var names = config.Props.Select(static x => x.Prop).ToArray();
        Assert.That(names, Does.Contain("prefix"));
        Assert.That(names, Has.None.EqualTo("checkforupdates").And.None.EqualTo("console.type"));

        AssertError(ApiEndpoints.SetConfig("bot", "CheckForUpdates", new SetConfigRequest("true"), registry), 403, "setting_locked");
        AssertError(ApiEndpoints.SetConfig("patron", "enabled", new SetConfigRequest("true"), registry), 403, "setting_locked");
        AssertError(ApiEndpoints.GetConfig("patron", registry), 404, "not_found");
        AssertError(ApiEndpoints.SetConfig("patron", "nope", new SetConfigRequest("true"), registry), 404, "not_found");

        var selfHosted = new ConfigRegistry([bot, Patron(false)], new BotPolicy(false));
        Assert.That(selfHosted.Names, Is.EqualTo(new[] { "bot", "patron" }));
        Assert.That(selfHosted.IsLocked("bot", "checkforupdates"), Is.False);
    }

    private static void AssertError(Microsoft.AspNetCore.Http.IResult result, int status, string code)
    {
        var json = (JsonHttpResult<ErrorDto>)result;
        Assert.That((json.StatusCode, json.Value!.Error), Is.EqualTo(((int?)status, code)));
    }

    [Test]
    public void FileValues_OutsideTheLimits_ResetOnManagedBots_AndOnlyBreakingOnesOnSelfHost_ClampedOnBoth()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nadeko-cfg-{System.Guid.NewGuid():N}.yml");
        try
        {
            System.IO.File.WriteAllText(path, "version: 1\nlimit: 500\npercent: 150\nsize: 0\nname: way too long\ntitle: ' '\n");

            var managed = new LimitedConfigService(path, new BotPolicy(true));
            Assert.That((managed.Data.Limit, managed.Data.Percent, managed.Data.Size, managed.Data.Name, managed.Data.Title),
                Is.EqualTo((10, 50, 1, "default", "title")));

            var selfHosted = new LimitedConfigService(path, new BotPolicy(false));
            Assert.That((selfHosted.Data.Limit, selfHosted.Data.Percent, selfHosted.Data.Size, selfHosted.Data.Name, selfHosted.Data.Title),
                Is.EqualTo((500, 50, 1, "way too long", "title")));

            System.IO.File.WriteAllText(path, "version: 1\nlimit: 7\npercent: 20\nname: ok\ntitle: t\n");
            managed.Reload();
            Assert.That((managed.Data.Limit, managed.Data.Percent, managed.Data.Name, managed.Data.Title),
                Is.EqualTo((7, 20, "ok", "t")));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    public sealed class LimitedConfig
    {
        public int Version { get; set; } = 1;
        public int Limit { get; set; } = 10;
        public int Percent { get; set; } = 50;
        public int Size { get; set; } = 10;
        public string Name { get; set; } = "default";
        public string Title { get; set; } = "title";
    }

    private sealed class LimitedConfigService : ConfigServiceBase<LimitedConfig>
    {
        public override string Name
            => "limited";

        public LimitedConfigService(string path, BotPolicy policy)
            : base(path, new YamlSeria(), Substitute.For<IPubSub>(), new("config.limited.updated"), policy)
        {
            AddNumberProp("limit", static c => c.Limit, static (c, v) => c.Limit = v, int.TryParse,
                ConfigPrinters.ToString, null, min: 1, max: 100);
            AddNumberProp("percent", static c => c.Percent, static (c, v) => c.Percent = v, int.TryParse,
                ConfigPrinters.ToString, null, min: 0, max: 100, strict: true);
            AddNumberProp("size", static c => c.Size, static (c, v) => c.Size = v, int.TryParse,
                ConfigPrinters.ToString, null, min: 1, max: null, clamp: true);
            AddTextProp("name", static c => c.Name, static (c, v) => c.Name = v, maxLength: 8);
            AddTextProp("title", static c => c.Title, static (c, v) => c.Title = v, maxLength: 8, required: true);
        }
    }
}
