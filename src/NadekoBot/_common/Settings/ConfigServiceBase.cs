using NadekoBot.Common.Configs;
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
    private readonly Lock _writeLock = new();

    private TSettings _data;

    private readonly Dictionary<string, Func<TSettings, string, TSettings?>> _propSetters = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, Func<TSettings, object>> _propSelectors = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, Func<object, string>> _propPrinters = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, string?> _propComments = new(StringComparer.InvariantCultureIgnoreCase);

    protected ConfigServiceBase(
        string filePath,
        IConfigSeria serializer,
        IPubSub pubSub,
        TypedKey<TSettings> changeKey)
    {
        _filePath = filePath;
        _serializer = serializer;
        _pubSub = pubSub;
        _changeKey = changeKey;

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

        try
        {
            Volatile.Write(ref _data, _serializer.Deserialize<TSettings>(File.ReadAllText(_filePath)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while loading {ConfigFilePath}", _filePath);
            throw;
        }
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

    protected void AddParsedProp<TProp>(
        string key,
        Func<TSettings, TProp> getter,
        Action<TSettings, TProp> setter,
        SettingParser<TProp> parser,
        Func<TProp, string> printer,
        string? comment = null,
        Func<TProp, bool>? checker = null)
    {
        checker ??= static _ => true;
        _propPrinters[key] = obj => printer((TProp)obj);
        _propSelectors[key] = cfg => getter(cfg)!;
        _propComments[key] = comment;
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