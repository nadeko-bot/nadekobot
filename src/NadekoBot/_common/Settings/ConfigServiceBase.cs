using NadekoBot.Common.Configs;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NadekoBot.Services;

public abstract class ConfigServiceBase<TSettings> : IConfigService
    where TSettings : class, new()
{
    public TSettings Data
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _data);
    }

    public abstract string Name { get; }
    protected readonly string _filePath;
    protected readonly IConfigSeria _serializer;
    protected readonly IPubSub _pubSub;
    private readonly TypedKey<TSettings> _changeKey;
    private readonly BotPolicy _policy;
    private readonly Lock _writeLock = new();

    private TSettings _data;

    private readonly Dictionary<string, Func<TSettings, string, TSettings?>> _propSetters = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, Func<TSettings, object>> _propSelectors = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, Func<object, string>> _propPrinters = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, string?> _propComments = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, ConfigPropInfo> _propInfos = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly List<Action<TSettings, TSettings>> _propValidators = new();

    protected ConfigServiceBase(
        string filePath,
        IConfigSeria serializer,
        IPubSub pubSub,
        TypedKey<TSettings> changeKey,
        BotPolicy policy)
    {
        _filePath = filePath;
        _serializer = serializer;
        _pubSub = pubSub;
        _changeKey = changeKey;
        _policy = policy;

        _data = new();
        Load();
        _pubSub.Sub(_changeKey, OnChangePublished);
    }

    private void PublishChange()
        => _pubSub.Pub(_changeKey, Data);

    private ValueTask OnChangePublished(TSettings newData)
    {
        Volatile.Write(ref _data, newData);
        OnStateUpdate();
        return default;
    }

    protected void Load()
    {
        if (!File.Exists(_filePath))
        {
            Volatile.Write(ref _data, new());
            Save();
        }

        TSettings data;
        try
        {
            data = _serializer.Deserialize<TSettings>(File.ReadAllText(_filePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while loading {ConfigFilePath}", _filePath);
            throw;
        }

        if (_propValidators.Count > 0)
        {
            var defaults = new TSettings();
            foreach (var validate in _propValidators)
                validate(data, defaults);
        }

        Volatile.Write(ref _data, data);
    }

    public void Reload()
    {
        Load();
        PublishChange();
    }

    protected virtual void OnStateUpdate()
    {
    }

    private void Save()
    {
        var strData = _serializer.Serialize(Data);
        File.WriteAllText(_filePath, strData);
    }

    // Text props go through AddTextProp and numeric props through AddNumberProp, so that each one declares its limits.
    protected void AddParsedProp<TProp>(
        string key,
        Func<TSettings, TProp> getter,
        Action<TSettings, TProp> setter,
        SettingParser<TProp> parser,
        Func<TProp, string> printer,
        string? comment = null,
        Func<TProp, bool>? checker = null,
        ConfigPropType? type = null)
        => AddPropInternal(key,
            getter,
            setter,
            parser,
            printer,
            comment,
            checker,
            null,
            null,
            type is { } t ? new(t) : InferInfoInternal<TProp>());

    protected void AddTextProp(
        string key,
        Func<TSettings, string> getter,
        Action<TSettings, string> setter,
        int maxLength,
        string? comment = null,
        Func<string, bool>? checker = null,
        bool required = false)
    {
        Func<string, bool> check = (checker, required) switch
        {
            (null, false) => value => value.Length <= maxLength,
            (null, true) => value => value.Length <= maxLength && !string.IsNullOrWhiteSpace(value),
            (_, false) => value => value.Length <= maxLength && checker(value),
            _ => value => value.Length <= maxLength && !string.IsNullOrWhiteSpace(value) && checker(value),
        };

        AddPropInternal(key,
            getter,
            setter,
            ConfigParsers.String,
            ConfigPrinters.ToString,
            comment,
            check,
            required ? static value => !string.IsNullOrWhiteSpace(value) : null,
            null,
            new(ConfigPropType.Text, MaxLength: maxLength));
    }

    protected void AddNumberProp<TProp>(
        string key,
        Func<TSettings, TProp> getter,
        Action<TSettings, TProp> setter,
        SettingParser<TProp> parser,
        Func<TProp, string> printer,
        string? comment,
        TProp? min,
        TProp? max,
        bool strict = false,
        bool clamp = false)
        where TProp : struct, INumber<TProp>
    {
        Func<TProp, bool> check = value => (min is not { } lo || value >= lo) && (max is not { } hi || value <= hi);
        Func<TProp, TProp>? clampValue = clamp
            ? value => min is { } lo && value < lo ? lo : max is { } hi && value > hi ? hi : value
            : null;

        var type = typeof(TProp) == typeof(decimal) || typeof(TProp) == typeof(double) || typeof(TProp) == typeof(float)
            ? ConfigPropType.Decimal
            : ConfigPropType.Number;

        AddPropInternal(key,
            getter,
            setter,
            parser,
            printer,
            comment,
            check,
            strict ? check : null,
            clampValue,
            new(type,
                min is { } mn ? double.CreateChecked(mn) : null,
                max is { } mx ? double.CreateChecked(mx) : null));
    }

    private static ConfigPropInfo InferInfoInternal<TProp>()
    {
        var t = typeof(TProp);
        if (t == typeof(bool))
            return new(ConfigPropType.Switch);
        if (t == typeof(Rgba32))
            return new(ConfigPropType.Color);
        if (t == typeof(CultureInfo))
            return new(ConfigPropType.Language);
        if (t == typeof(TimeZoneInfo))
            return new(ConfigPropType.Timezone);
        if (t.IsEnum)
            return new(ConfigPropType.Choice, Options: Enum.GetNames(t));
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float))
            return new(ConfigPropType.Decimal);
        if (t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong) || t == typeof(short))
            return new(ConfigPropType.Number);

        return new(ConfigPropType.Text);
    }

    private void AddPropInternal<TProp>(
        string key,
        Func<TSettings, TProp> getter,
        Action<TSettings, TProp> setter,
        SettingParser<TProp> parser,
        Func<TProp, string> printer,
        string? comment,
        Func<TProp, bool>? checker,
        Func<TProp, bool>? selfHostChecker,
        Func<TProp, TProp>? clamp,
        ConfigPropInfo info)
    {
        checker ??= static _ => true;
        // The owner of a self-hosted bot can edit the file anyway, so only a value which breaks the bot is reset there.
        var loadChecker = _policy.Managed ? checker : selfHostChecker;
        _propPrinters[key] = obj => printer((TProp)obj);
        _propSelectors[key] = cfg => getter(cfg)!;
        _propComments[key] = comment;
        _propInfos[key] = info;

        // A file value skips the checker of .config, so it is checked on load too.
        void Validate(TSettings config, TSettings defaults)
        {
            try
            {
                var value = getter(config);
                if (value is null || checker(value))
                    return;

                if (clamp is not null)
                {
                    var clamped = clamp(value);
                    Log.Warning("Config {ConfigFile} {Prop} value {Value} is outside its limits. Using {Clamped}",
                        _filePath,
                        key,
                        value,
                        clamped);
                    setter(config, clamped);
                    return;
                }

                if (loadChecker is null || loadChecker(value))
                {
                    Log.Warning("Config {ConfigFile} {Prop} value {Value} is outside the limits of .config",
                        _filePath,
                        key,
                        value);
                    return;
                }

                Log.Warning("Config {ConfigFile} {Prop} value {Value} is outside its limits. Using the default",
                    _filePath,
                    key,
                    value);
                setter(config, getter(defaults));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to check config {ConfigFile} {Prop}", _filePath, key);
            }
        }

        _propValidators.Add(Validate);
        Validate(_data, new TSettings());

        _propSetters[key] = (config, input) =>
        {
            if (!parser(input, out var value))
                return default;

            if (!checker(value))
                return default;

            setter(config, value);
            return config;
        };
    }

    public IReadOnlyList<string> GetSettableProps()
        => _propSetters.Keys.ToList();

    public string? GetSetting(string prop)
    {
        if (!_propSelectors.TryGetValue(prop, out var selector) || !_propPrinters.TryGetValue(prop, out var printer))
            return null;

        return printer(selector(Data));
    }

    public string? GetComment(string prop)
    {
        if (_propComments.TryGetValue(prop, out var comment))
            return comment;

        return null;
    }

    public ConfigPropInfo? GetPropInfo(string prop)
        => _propInfos.GetValueOrDefault(prop);

    public bool SetSetting(string prop, string newValue)
    {
        if (!_propSetters.TryGetValue(prop, out var setter))
            return false;

        lock (_writeLock)
        {
            var copy = _serializer.Deserialize<TSettings>(_serializer.Serialize(Data));
            var result = setter(copy, newValue);
            if (result is null)
                return false;

            Volatile.Write(ref _data, result);
            Save();
        }

        PublishChange();
        return true;
    }

    public void ModifyConfig(Action<TSettings> action)
    {
        lock (_writeLock)
        {
            var copy = _serializer.Deserialize<TSettings>(_serializer.Serialize(Data));
            action(copy);
            Volatile.Write(ref _data, copy);
            Save();
        }

        PublishChange();
    }
}