#nullable disable
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NadekoBot.Services;

public sealed class YtdlOperation
{
    private const string COOKIES_PATH = "data/ytcookies.txt";

    private readonly string[] _baseArgs;

    public YtdlOperation(string[] baseArgs)
    {
        _baseArgs = baseArgs;
    }

    private Process CreateProcess(string[] userArgs)
    {
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

        if (File.Exists(COOKIES_PATH))
        {
            psi.ArgumentList.Add("--cookies");
            psi.ArgumentList.Add(COOKIES_PATH);
        }

        foreach (var a in _baseArgs)
            psi.ArgumentList.Add(a);

        foreach (var a in userArgs)
            psi.ArgumentList.Add(a);

        return new() { StartInfo = psi };
    }

    public async Task<string> GetDataAsync(params string[] args)
    {
        try
        {
            using var process = CreateProcess(args);

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
        using var process = CreateProcess(args);

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