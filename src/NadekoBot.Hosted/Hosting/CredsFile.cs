using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NadekoBot.Common.Yml;
using YamlDotNet.Core;

namespace NadekoBot.Hosted;

public static class CredsFile
{
    public const int MAX_TOKEN_LENGTH = 256;

    private const string ENV_PREFIX = "bot_";
    private const string SQLITE = "sqlite";
    private const string BROKEN_SUFFIX = ".broken";

    public static Creds? Read(string path)
        => File.Exists(path)
            ? Yaml.Deserializer.Deserialize<Creds?>(File.ReadAllText(path))
            : null;

    public static bool TryRead(string path, out Creds? creds)
    {
        try
        {
            creds = Read(path);
            return true;
        }
        catch (YamlException ex)
        {
            Log.Error(ex, "{Path} is not valid YAML", path);
            creds = null;
            return false;
        }
    }

    // The same sources as BotCredsProvider, so the host sees the token and the database the bot will use.
    public static Creds LoadEffective(string path)
    {
        try
        {
            return BindInternal(new ConfigurationBuilder().AddYamlFile(path, optional: true, reloadOnChange: false));
        }
        catch (Exception ex) when (ex is YamlException or FormatException or InvalidDataException)
        {
            Log.Error(ex, "{Path} is not valid YAML. Using only the {Prefix} environment variables", path, ENV_PREFIX);
            return BindInternal(new ConfigurationBuilder());
        }
    }

    private static Creds BindInternal(IConfigurationBuilder builder)
    {
        var creds = new Creds();
        builder.AddEnvironmentVariables(ENV_PREFIX).Build().Bind(creds);
        return creds;
    }

    // An empty token clears it. Discord tokens are printable ASCII without spaces.
    public static bool IsValidToken(ReadOnlySpan<char> token)
        => token.Length <= MAX_TOKEN_LENGTH && !token.ContainsAnyExceptInRange('!', '~');

    public const int MAX_AI_KEY_LENGTH = 512;

    public static bool IsValidAiKey(ReadOnlySpan<char> key)
        => key.Length is > 0 and <= MAX_AI_KEY_LENGTH && !key.ContainsAnyExceptInRange('!', '~');

    public static void WriteToken(string path, string token)
        => UpdateInternal(path, c => c.Token = token);

    public static void WriteOwners(string path, ulong[] owners)
        => UpdateInternal(path, c => c.OwnerIds = [..owners]);

    // One write, so a token never applies without the owners which came with it.
    public static void WriteTokenAndOwners(string path, string? token, ulong[]? owners)
        => UpdateInternal(path, c =>
        {
            if (token is not null)
                c.Token = token;

            if (owners is not null)
                c.OwnerIds = [..owners];
        });

    // A null key also clears the legacy gpt3ApiKey, which the bot uses when aiApiKey is empty.
    public static void WriteAiKey(string path, string? key)
        => UpdateInternal(path, c =>
        {
            c.AiApiKey = key ?? string.Empty;
            if (key is null)
                c.Gpt3ApiKey = string.Empty;
        });

    private static void UpdateInternal(string path, Action<Creds> update)
    {
        if (!TryRead(path, out var creds))
        {
            // Keep the unreadable file for the owner instead of dropping the rest of their settings silently.
            File.Copy(path, path + BROKEN_SUFFIX, overwrite: true);
            Log.Warning("Replaced the unreadable {Path}. The old file is {Broken}", path, path + BROKEN_SUFFIX);
        }

        creds ??= new Creds();
        update(creds);
        SecretFile.WriteAllText(path, Yaml.Serializer.Serialize(creds));
    }

    public static string? SqliteDbPath(Creds creds, string workingDir)
    {
        var db = creds.Db;
        if (db is not null && !string.Equals(db.Type, SQLITE, StringComparison.OrdinalIgnoreCase))
            return null;

        var connectionString = string.IsNullOrWhiteSpace(db?.ConnectionString)
            ? new Creds().Db.ConnectionString
            : db.ConnectionString;

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(dataSource)
            ? null
            : Path.GetFullPath(dataSource, workingDir);
    }
}

public sealed class CredsStore(ApiKey key, ApiSettings settings, HostPaths paths, BootGuard boot, LogStreamService logs)
{
    // The key check and both file writes are one step, so a racing first-use PUT cannot replace the key.
    private readonly SemaphoreSlim _write = new(1, 1);

    public async Task<bool> TrySetAsync(string authorizationHeader, string apiKey, string? token, ulong[]? owners = null)
    {
        await _write.WaitAsync();
        try
        {
            if (key.IsSet && !key.Matches(authorizationHeader))
                return false;

            var writesCreds = token is not null || owners is not null;
            var previousCreds = writesCreds && File.Exists(paths.Creds) ? File.ReadAllText(paths.Creds) : null;
            if (writesCreds)
                CredsFile.WriteTokenAndOwners(paths.Creds, token, owners);

            try
            {
                ApiSettingsFile.Save(paths.ApiSettings, settings.WithKey(apiKey));
            }
            catch when (writesCreds)
            {
                // New creds with the old key would apply on some later restart, after the caller got a 500.
                if (previousCreds is not null)
                    SecretFile.WriteAllText(paths.Creds, previousCreds);
                else
                    File.Delete(paths.Creds);
                throw;
            }

            logs.AddSecrets(token, apiKey);
            key.Set(apiKey);
            boot.Reset();
            return true;
        }
        finally
        {
            _write.Release();
        }
    }

    public async Task SetOwnersAsync(ulong[] owners)
    {
        await _write.WaitAsync();
        try
        {
            CredsFile.WriteOwners(paths.Creds, owners);
        }
        finally
        {
            _write.Release();
        }
    }

    public async Task SetAiKeyAsync(string? aiKey)
    {
        await _write.WaitAsync();
        try
        {
            CredsFile.WriteAiKey(paths.Creds, aiKey);
        }
        finally
        {
            _write.Release();
        }
    }
}
