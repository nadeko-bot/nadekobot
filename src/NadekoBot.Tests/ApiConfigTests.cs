using System.Collections.Generic;
using Microsoft.AspNetCore.Http.HttpResults;
using NadekoBot.Common;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiConfigTests
{
    private sealed class FakeConfigService : IConfigService
    {
        private readonly Dictionary<string, string> _values;
        private readonly Dictionary<string, string> _comments;

        public FakeConfigService(
            string name,
            Dictionary<string, string> values,
            Dictionary<string, string> comments = null)
        {
            Name = name;
            _values = values;
            _comments = comments ?? new Dictionary<string, string>();
        }

        public string Name { get; }
        public string LastSetProp { get; private set; } = "";
        public string LastSetValue { get; private set; } = "";
        public bool NextSetResult { get; set; } = true;

        public void Reload() { }

        public IReadOnlyList<string> GetSettableProps()
            => new List<string>(_values.Keys);

        public string GetSetting(string prop)
            => _values.TryGetValue(prop, out var v) ? v : null;

        public string GetComment(string prop)
            => _comments.TryGetValue(prop, out var c) ? c : null;

        public ConfigPropInfo GetPropInfo(string prop)
            => _values.ContainsKey(prop) ? new ConfigPropInfo(ConfigPropType.Number, Min: 1, Max: 10) : null;

        public bool SetSetting(string prop, string newValue)
        {
            LastSetProp = prop;
            LastSetValue = newValue;
            if (NextSetResult)
                _values[prop] = newValue;
            return NextSetResult;
        }
    }

    private static ConfigRegistry Lookup(params IConfigService[] svcs)
        => new(svcs, new BotPolicy(false));

    [Test]
    public void Registry_ExcludesMedusaAndSortsNames()
    {
        var lookup = Lookup(
            new FakeConfigService("xp", new()),
            new FakeConfigService("medusa", new()),
            new FakeConfigService("Gambling", new()));

        Assert.That(lookup.Names, Is.EqualTo(new[] { "Gambling", "xp" }));
        Assert.That(lookup.TryGet("medusa", out _, out _), Is.False);
    }

    [Test]
    public void GetConfig_CaseInsensitive_ReturnsPropsWithValuesAndComments()
    {
        var svc = new FakeConfigService(
            "gambling",
            new() { ["betroll.rolls"] = "3" },
            new() { ["betroll.rolls"] = "how many rolls" });
        var lookup = Lookup(svc);

        var result = ApiEndpoints.GetConfig("GamBLing", lookup);

        var json = (JsonHttpResult<ConfigDto>)result;
        Assert.That(json.Value!.Name, Is.EqualTo("gambling"));
        Assert.That(json.Value.Props, Has.Count.EqualTo(1));
        Assert.That(json.Value.Props[0].Prop, Is.EqualTo("betroll.rolls"));
        Assert.That(json.Value.Props[0].Value, Is.EqualTo("3"));
        Assert.That(json.Value.Props[0].Comment, Is.EqualTo("how many rolls"));
        Assert.That((json.Value.Props[0].Type, json.Value.Props[0].Min, json.Value.Props[0].Max),
            Is.EqualTo(("number", (double?)1, (double?)10)));
    }

    [Test]
    public void SetConfig_WritesKnownPropsAndRejectsTheRest()
    {
        var svc = new FakeConfigService("gambling", new() { ["betroll.rolls"] = "3" });

        var ok = (JsonHttpResult<ConfigPropDto>)ApiEndpoints.SetConfig("GAMBLING", "BetRoll.Rolls", new SetConfigRequest("5"), Lookup(svc));
        Assert.That((ok.Value!.Prop, ok.Value.Value), Is.EqualTo(("betroll.rolls", "5")));
        Assert.That((svc.LastSetProp, svc.LastSetValue), Is.EqualTo(("betroll.rolls", "5")));

        AssertError(ApiEndpoints.GetConfig("missing", Lookup(svc)), 404, "not_found");
        AssertError(ApiEndpoints.SetConfig("gambling", "nope", new SetConfigRequest("5"), Lookup(svc)), 404, "prop_not_found");

        svc.NextSetResult = false;
        AssertError(ApiEndpoints.SetConfig("gambling", "betroll.rolls", new SetConfigRequest("bad"), Lookup(svc)), 400, "invalid_value");
    }

    private static void AssertError(Microsoft.AspNetCore.Http.IResult result, int status, string code)
    {
        var json = (JsonHttpResult<ErrorDto>)result;
        Assert.That((json.StatusCode, json.Value!.Error), Is.EqualTo(((int?)status, code)));
    }
}
