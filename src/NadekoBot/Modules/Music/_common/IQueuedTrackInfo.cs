#nullable disable
namespace NadekoBot.Modules.Music;

public interface IQueuedTrackInfo : ITrackInfo
{
    public ITrackInfo TrackInfo { get; }

    public string Queuer { get; }

    public bool AllowLocalNetwork { get; }
}