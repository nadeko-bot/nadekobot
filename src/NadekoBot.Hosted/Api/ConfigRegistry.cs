using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Hosted;

// Config services and their settable props never change after the bot starts, so the lookups are built once.
public sealed class ConfigRegistry
{
    private const string MEDUSA_CONFIG = "medusa";

    private readonly FrozenDictionary<string, Entry> _configs;
    private readonly FrozenDictionary<string, string[]> _lockedProps;

    public IReadOnlyList<string> Names { get; }

    public ConfigRegistry(IEnumerable<IConfigService> configs, BotPolicy policy)
    {
        // Config names are ASCII identifiers.
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var locked = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            if (config.Name == MEDUSA_CONFIG)
                continue;

            var props = config.GetSettableProps();
            var open = new List<string>(props.Count);
            var closed = new List<string>();
            foreach (var prop in props)
            {
                if (policy.IsLocked(config.Name, prop))
                    closed.Add(prop);
                else
                    open.Add(prop);
            }

            if (closed.Count > 0)
                locked[config.Name] = closed.ToArray();

            // A config with nothing left to change is not listed at all.
            if (open.Count == 0 && closed.Count > 0)
                continue;

            entries[config.Name] = new(config, open.ToArray());
        }

        _configs = entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _lockedProps = locked.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        var all = new string[_configs.Count];
        var n = 0;
        foreach (var kvp in _configs)
            all[n++] = kvp.Key;

        Array.Sort(all, StringComparer.OrdinalIgnoreCase);
        Names = all;
    }

    public bool TryGet(string name, [MaybeNullWhen(false)] out IConfigService config, out string[] props)
    {
        if (_configs.TryGetValue(name, out var entry))
        {
            config = entry.Config;
            props = entry.Props;
            return true;
        }

        config = null;
        props = [];
        return false;
    }

    public bool IsLocked(string name, string prop)
        => _lockedProps.TryGetValue(name, out var locked) && TryGetProp(locked, prop, out _);

    public static bool TryGetProp(string[] props, string prop, [MaybeNullWhen(false)] out string canonical)
    {
        foreach (var p in props)
        {
            if (string.Equals(p, prop, StringComparison.OrdinalIgnoreCase))
            {
                canonical = p;
                return true;
            }
        }

        canonical = null;
        return false;
    }

    private readonly record struct Entry(IConfigService Config, string[] Props);
}

public static class ConfigPropTypes
{
    public const string TEXT = "text";
    public const string NUMBER = "number";
    public const string DECIMAL = "decimal";
    public const string SWITCH = "switch";
    public const string COLOR = "color";
    public const string CHOICE = "choice";
    public const string LANGUAGE = "language";
    public const string TIMEZONE = "timezone";

    public static string Of(ConfigPropType type)
        => type switch
        {
            ConfigPropType.Number => NUMBER,
            ConfigPropType.Decimal => DECIMAL,
            ConfigPropType.Switch => SWITCH,
            ConfigPropType.Color => COLOR,
            ConfigPropType.Choice => CHOICE,
            ConfigPropType.Language => LANGUAGE,
            ConfigPropType.Timezone => TIMEZONE,
            _ => TEXT
        };
}
