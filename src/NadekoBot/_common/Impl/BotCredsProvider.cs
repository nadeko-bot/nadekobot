#nullable disable
using Microsoft.Extensions.Configuration;
using NadekoBot.Common.Yml;

namespace NadekoBot.Services;

public sealed class BotCredsProvider : IBotCredsProvider
{
    private const string CREDS_FILE_NAME = "data/creds.yml";
    private const string CREDS_EXAMPLE_FILE_NAME = "data/creds_example.yml";

    private string CredsPath { get; }

    private string CredsExamplePath { get; }

    private readonly int? _totalShards;


    private readonly Creds _creds = new();
    private readonly IConfigurationRoot _config;


    private readonly object _reloadLock = new();

    public BotCredsProvider(int? totalShards = null)
    {
        _totalShards = totalShards;

        CredsPath = Path.Combine(Directory.GetCurrentDirectory(), CREDS_FILE_NAME);
        CredsExamplePath = Path.Combine(Directory.GetCurrentDirectory(), CREDS_EXAMPLE_FILE_NAME);

        if (!File.Exists(CredsExamplePath))
        {
            Log.Information("Creating data/creds_example.yml file...");
            try
            {
                File.WriteAllText(CredsExamplePath, Yaml.Serializer.Serialize(_creds));
            }
            catch (Exception ex)
            {
                // this can fail in docker containers
                Log.Error(ex, "Error creating data/creds_example.yml file");
            }
        }


        try
        {
            MigrateCredentials();

            if (!File.Exists(CredsPath))
            {
                Log.Warning(
                    "{CredsPath} is missing. Attempting to load creds from environment variables prefixed with 'NadekoBot_'. Example is in {CredsExamplePath}",
                    CredsPath,
                    CredsExamplePath);
            }

            _config = new ConfigurationBuilder().AddYamlFile(CredsPath, false, true)
                .AddEnvironmentVariables("bot_")
                .Build();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading data/creds.yml file");
        }

        Reload();
    }

    public void Reload()
    {
        lock (_reloadLock)
        {
            _creds.OwnerIds.Clear();
            _config.Bind(_creds);

            if (string.IsNullOrWhiteSpace(_creds.Token))
            {
                Log.Error("Token is missing from data/creds.yml or Environment variables.\nAdd it and restart the program");
                Helpers.ReadErrorAndExit(1);
                return;
            }
            
            if (string.IsNullOrWhiteSpace(_creds.RestartCommand?.Cmd)
                || string.IsNullOrWhiteSpace(_creds.RestartCommand?.Args))
            {
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    _creds.RestartCommand = new RestartConfig()
                    {
                        Args = "dotnet",
                        Cmd = "NadekoBot.dll -- {0}"
                    };
                }
                else
                {
                    _creds.RestartCommand = new RestartConfig()
                    {
                        Args = "NadekoBot.exe",
                        Cmd = "{0}"
                    };
                }
            }

            if (string.IsNullOrWhiteSpace(_creds.RedisOptions))
                _creds.RedisOptions = "127.0.0.1,syncTimeout=3000";

            // replace the old generated key with the shared key
            if (string.IsNullOrWhiteSpace(_creds.CoinmarketcapApiKey)
                || _creds.CoinmarketcapApiKey.StartsWith("e79ec505-0913"))
                _creds.CoinmarketcapApiKey = "3077537c-7dfb-4d97-9a60-56fc9a9f5035";

            _creds.TotalShards = _totalShards ?? _creds.TotalShards;
        }
    }

    public void ModifyCredsFile(Action<IBotCreds> func)
    {
        var ymlData = File.ReadAllText(CREDS_FILE_NAME);
        var creds = Yaml.Deserializer.Deserialize<Creds>(ymlData);

        func(creds);

        ymlData = Yaml.Serializer.Serialize(creds);
        File.WriteAllText(CREDS_FILE_NAME, ymlData);
    }

    private void MigrateCredentials()
    {
        if (File.Exists(CREDS_FILE_NAME))
        {
            var creds = Yaml.Deserializer.Deserialize<Creds>(File.ReadAllText(CREDS_FILE_NAME));
            if (creds.Version < 20)
            {
                creds.Version = 20;
                File.WriteAllText(CREDS_FILE_NAME, Yaml.Serializer.Serialize(creds));
            }
        }
    }

    public IBotCreds GetCreds()
    {
        lock (_reloadLock)
        {
            return _creds;
        }
    }
}