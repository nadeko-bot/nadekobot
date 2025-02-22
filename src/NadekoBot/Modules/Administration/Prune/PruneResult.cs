#nullable disable
namespace NadekoBot.Modules.Administration.Services;

public enum PruneResult
{
    Success,
    AlreadyRunning,
    FeatureLimit,
}