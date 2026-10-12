#nullable disable
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NadekoBot.Services;

public sealed class YtdlOperation
{
    private const string COOKIES_PATH = "data/ytcookies.txt";

    private readonly string[] _baseArgs;
    private readonly bool _bareHostIsUrl;
    private readonly bool _ignoreConfig;
    private readonly PublicOnlyProxy _proxy;

    public YtdlOperation(string[] baseArgs, BotPolicy policy, PublicOnlyProxy proxy)
    {
        _baseArgs = baseArgs;
        _ignoreConfig = policy.Managed;
        _proxy = proxy;
        _bareHostIsUrl = Array.IndexOf(baseArgs, "--default-search") < 0;
    }

    // Returns null when the proxy cannot start, so a member's query never runs without it.
    private Process CreateProcess(string[] userArgs, bool allowLocalNetwork)
    {
        string proxyUrl = null;
        if (!allowLocalNetwork && !_proxy.TryGetUrl(out proxyUrl))
            return null;

        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        // A config file in the working directory could make yt-dlp run any program.
        if (_ignoreConfig)
            psi.ArgumentList.Add("--ignore-config");

        if (File.Exists(COOKIES_PATH))
        {
            psi.ArgumentList.Add("--cookies");
            psi.ArgumentList.Add(COOKIES_PATH);
        }

        if (proxyUrl is not null)
        {
            psi.ArgumentList.Add("--proxy");
            psi.ArgumentList.Add(proxyUrl);
            foreach (var name in PublicOnlyProxy.BypassEnvVars)
                psi.Environment.Remove(name);
        }

        foreach (var a in _baseArgs)
            psi.ArgumentList.Add(a);

        foreach (var a in userArgs)
            psi.ArgumentList.Add(a);

        return new() { StartInfo = psi };
    }

    // Refuses a local link early. Redirects and DNS rebinding are stopped by PublicOnlyProxy, not by this check.
    // Without --default-search, yt-dlp also treats "host.tld/path" as an http URL.
    public static bool IsAllowedTarget(string arg, bool bareHostIsUrl)
    {
        var span = arg.AsSpan().Trim();
        if (span.Contains("://", StringComparison.Ordinal))
            return UrlExtensions.IsPublicUrl(span.ToString());

        if (!bareHostIsUrl)
            return true;

        var slash = span.IndexOf('/');
        if (slash > 0 && span[..slash].IndexOfAny(' ', '\t') < 0 && span[..slash].Contains('.'))
            return UrlExtensions.IsPublicUrl("http://" + span.ToString());

        return true;
    }

    public bool IsAllowedTarget(string arg)
        => IsAllowedTarget(arg, _bareHostIsUrl);

    private bool AreAllowedTargets(string[] args)
    {
        foreach (var a in args)
        {
            if (!IsAllowedTarget(a, _bareHostIsUrl))
            {
                Log.Information("yt-dlp refused a non-public target: {Target}", a);
                return false;
            }
        }

        return true;
    }

    public Task<string> GetDataAsync(params string[] args)
        => GetDataInternalAsync(args, false);

    public Task<string> GetDataAsync(string arg, bool allowLocalNetwork)
        => GetDataInternalAsync([arg], allowLocalNetwork);

    private async Task<string> GetDataInternalAsync(string[] args, bool allowLocalNetwork)
    {
        if (!allowLocalNetwork && !AreAllowedTargets(args))
            return default;

        try
        {
            using var process = CreateProcess(args, allowLocalNetwork);
            if (process is null)
                return default;

            Log.Debug("Executing {FileName} {Arguments}", process.StartInfo.FileName, process.StartInfo.Arguments);
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var str = await stdoutTask;
            var err = await stderrTask;
            if (!string.IsNullOrEmpty(err))
                Log.Warning("yt-dlp warning: {YtdlWarning}", err);

            return str;
        }
        catch (Win32Exception)
        {
            Log.Error("yt-dlp is likely not installed. Please install it before running the command again");
            return default;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception running yt-dlp: {ErrorMessage}", ex.Message);
            return default;
        }
    }

    public async IAsyncEnumerable<string> EnumerateDataAsync(params string[] args)
    {
        if (!AreAllowedTargets(args))
            yield break;

        using var process = CreateProcess(args, false);
        if (process is null)
            yield break;

        Log.Debug("Executing {FileName} {Arguments}", process.StartInfo.FileName, process.StartInfo.Arguments);
        process.Start();

        try
        {
            string line;
            while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
                yield return line;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }
        }
    }
}