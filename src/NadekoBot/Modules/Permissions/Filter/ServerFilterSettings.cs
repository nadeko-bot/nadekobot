#nullable disable
namespace NadekoBot.Modules.Permissions.Services;

public readonly struct ServerFilterSettings
{
    public bool FilterInvitesEnabled { get; init; }
    public bool FilterLinksEnabled { get; init; }
    public IReadOnlyCollection<ulong> FilterInvitesChannels { get; init; }
    public IReadOnlyCollection<ulong> FilterLinksChannels { get; init; }
}