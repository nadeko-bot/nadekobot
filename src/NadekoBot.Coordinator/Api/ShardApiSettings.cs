#nullable enable
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NadekoBot.Coordinator.Api;

// The shards' own data/api.yml, so the coordinator and the shards have one key and one port list.
public sealed class ShardApiFile
{
    public string? Address { get; set; }
    public int Port { get; set; }
    public string? ApiKey { get; set; }
}

public sealed record ShardApiTarget(byte[] KeyHash, string Host, int BasePort)
{
    public bool HasKey
        => KeyHash.Length > 0;

    public string ShardPrefix(int shardId)
        => $"http://{Host}:{BasePort + shardId}/";
}

public sealed class ShardApiSettings
{
    public const string PATH = "data/api.yml";
    private const int MAX_PORT = 65535;

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
                                                          .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                                          .IgnoreUnmatchedProperties()
                                                          .Build();

    private volatile ShardApiTarget _current = new([], "127.0.0.1", 0);

    public ShardApiTarget Current
        => _current;

    private readonly CoordApiEndpoint _endpoint;

    public ShardApiSettings(CoordApiEndpoint endpoint)
    {
        _endpoint = endpoint;
        Reload();
    }

    public void Reload()
    {
        if (!_endpoint.Enabled)
            return;

        try
        {
            if (!File.Exists(PATH))
            {
                Log.Warning("No {Path}. The dashboard API refuses every request", PATH);
                _current = new([], "127.0.0.1", 0);
                return;
            }

            var file = _deserializer.Deserialize<ShardApiFile?>(File.ReadAllText(PATH)) ?? new();
            if (file.Port is < 1 or > MAX_PORT)
            {
                Log.Error("{Path} has no valid port. The dashboard API refuses every request", PATH);
                _current = new([], "127.0.0.1", 0);
                return;
            }

            var key = file.ApiKey ?? string.Empty;
            if (key.Length == 0)
                Log.Warning("{Path} has no apiKey. The dashboard API refuses every request", PATH);

            _current = new(
                key.Length == 0 ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(key)),
                ShardHost(file.Address),
                file.Port);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not read {Path}. The dashboard API keeps its last settings", PATH);
        }
    }

    // A shard which listens on every address also listens on loopback.
    private static string ShardHost(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !IPAddress.TryParse(address, out var ip)
            || ip.Equals(IPAddress.Any)
            || ip.Equals(IPAddress.IPv6Any))
            return "127.0.0.1";

        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{ip}]"
            : ip.ToString();
    }
}
