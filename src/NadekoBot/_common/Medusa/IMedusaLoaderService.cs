using System.Globalization;

namespace Nadeko.Common.Medusa;

public interface IMedusaLoaderService
{
    Task<MedusaLoadResult> LoadMedusaAsync(string medusaName);
    Task<MedusaUnloadResult> UnloadMedusaAsync(string medusaName);
    string GetCommandDescription(string medusaName, string commandName, CultureInfo culture);
    string[] GetCommandExampleArgs(string medusaName, string commandName, CultureInfo culture);
    Task ReloadStrings();
    IReadOnlyCollection<string> GetAllMedusae();
    IReadOnlyCollection<MedusaStats> GetLoadedMedusae(CultureInfo? cultureInfo = null);
}

public sealed record MedusaStats(string Name,
    string? Description,
    IReadOnlyCollection<SnekStats> Sneks);
    
public sealed record SnekStats(string Name,
    string? Prefix,
    IReadOnlyCollection<SnekCommandStats> Commands);

public sealed record SnekCommandStats(string Name);