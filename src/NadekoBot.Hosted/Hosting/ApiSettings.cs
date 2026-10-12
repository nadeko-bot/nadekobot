using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NadekoBot.Common.Yml;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NadekoBot.Hosted;

public sealed class ApiSettings
{
    public const int MIN_KEY_LENGTH = 32;
    public const int MAX_KEY_LENGTH = 512;
    public const int MAX_IMPORT_MB = 10 * 1024;
    public const int MIN_UPDATE_MB = 64;
    public const int MAX_UPDATE_MB = 2048;
    private const int MAX_PORT = 65535;

    // RFC 7235 token68, so the key fits in an Authorization header unchanged.
    private static readonly SearchValues<char> _keyChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~+/=");

    private string _apiKey = string.Empty;

    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 1;

    [Comment("""
             Address the API listens on. Default 127.0.0.1.
             Use 0.0.0.0 only when nothing but the manager can reach this machine.
             """)]
    public string Address { get; set; } = "127.0.0.1";

    [Comment("Port the API listens on. Under the coordinator each shard listens on port + shard id. Default 5010")]
    public int Port { get; set; } = 5010;

    [Comment("""
             Bearer key for every request. Leave empty on a new standalone bot:
             the manager sets it with the first PUT /api/creds.
             """)]
    public string ApiKey
    {
        get => _apiKey;
        // "apiKey:" with no value reads as null.
        set => _apiKey = value ?? string.Empty;
    }

    [Comment("Largest backup archive an import accepts, in megabytes. Default 256")]
    public int MaxImportMb { get; set; } = 256;

    [Comment("Largest release package PUT /api/update accepts, in megabytes. Default 512")]
    public int MaxUpdateMb { get; set; } = 512;

    [YamlIgnore]
    public bool HasKey
        => ApiKey.Length > 0;

    [YamlIgnore]
    public long MaxImportBytes
        => (long)MaxImportMb * 1024 * 1024;

    [YamlIgnore]
    public long MaxUpdateBytes
        => (long)MaxUpdateMb * 1024 * 1024;

    // A full copy, so a setting added later is never lost when the key changes.
    public ApiSettings WithKey(string apiKey)
    {
        var copy = (ApiSettings)MemberwiseClone();
        copy.ApiKey = apiKey;
        return copy;
    }

    public static bool IsValidKey(ReadOnlySpan<char> key)
    {
        if (key.Length is < MIN_KEY_LENGTH or > MAX_KEY_LENGTH || key.ContainsAnyExcept(_keyChars))
            return false;

        // token68 allows '=' only as trailing padding.
        var padding = key.IndexOf('=');
        return padding < 0 || !key[padding..].ContainsAnyExcept('=');
    }

    public string? Validate(int lastShardId)
    {
        if (!IPAddress.TryParse(Address, out _))
            return $"address '{Address}' is not an IP address";

        if (Port is < 1 or > MAX_PORT || Port + lastShardId > MAX_PORT)
            return $"port {Port} is out of range";

        if (MaxImportMb is < 1 or > MAX_IMPORT_MB)
            return $"maxImportMb must be between 1 and {MAX_IMPORT_MB}";

        if (MaxUpdateMb is < MIN_UPDATE_MB or > MAX_UPDATE_MB)
            return $"maxUpdateMb must be between {MIN_UPDATE_MB} and {MAX_UPDATE_MB}";

        if (HasKey && !IsValidKey(ApiKey))
            return $"apiKey must be {MIN_KEY_LENGTH} to {MAX_KEY_LENGTH} characters of A-Z a-z 0-9 - . _ ~ + / =";

        return null;
    }
}

// Holds only the SHA-256 of the key; empty while no key is set.
public sealed class ApiKey(string key)
{
    private volatile byte[] _hash = HashInternal(key);

    public bool IsSet
        => _hash.Length > 0;

    public bool Matches(ReadOnlySpan<char> authorizationHeader)
        => BearerAuth.Matches(authorizationHeader, _hash);

    public void Set(string key)
        => _hash = HashInternal(key);

    private static byte[] HashInternal(string key)
        => key.Length == 0 ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(key));
}

public static class ApiSettingsFile
{
    // Unlike the bot's configs, an unknown key is an error: a misspelled apiKey would leave the key blank.
    private static readonly IDeserializer _strict = new DeserializerBuilder()
                                                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                                    .Build();

    public static ApiSettings LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var created = new ApiSettings();
            Save(path, created);
            return created;
        }

        // A broken file must stop the start: defaults would open PUT /api/creds to anyone who reaches the port.
        try
        {
            return _strict.Deserialize<ApiSettings?>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"{path} is empty");
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"{path} is not valid YAML: {ex.Message}", ex);
        }
    }

    public static void Save(string path, ApiSettings settings)
        => SecretFile.WriteAllText(path, Yaml.Serializer.Serialize(settings));
}

public static class SecretFile
{
    private const UnixFileMode OWNER_ONLY = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // The temp file is created 0600 and synced before the rename, so a crash leaves the old file or the new one.
    public static void WriteAllText(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OWNER_ONLY;

        try
        {
            using (var fs = new FileStream(temp, options))
            {
                fs.Write(Encoding.UTF8.GetBytes(text));
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException)
            {
                Log.Warning(deleteEx, "Could not delete {Temp}", temp);
            }

            throw;
        }
    }
}
