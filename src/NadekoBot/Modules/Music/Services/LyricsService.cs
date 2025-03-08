using Musix;

namespace NadekoBot.Modules.Music;

public sealed class LyricsService(HttpClient client) : ILyricsService, INService
{
    private readonly MusixMatchAPI _api = new(client);

    private static string NormalizeName(string name)
        => string.Join("-", name.Split()
                .Select(x => new string(x.Where(c => char.IsLetterOrDigit(c)).ToArray())))
            .Trim('-');

    public async Task<IReadOnlyList<TracksItem>> SearchTracksAsync(string name)
        => await _api.SearchTracksAsync(NormalizeName(name))
            .Fmap(x => x
                .Message
                .Body
                .TrackList
                .Map(x => new TracksItem(x.Track.ArtistName, x.Track.TrackName, x.Track.TrackId)));

    public async Task<string> GetLyricsAsync(int trackId)
        => await _api.GetTrackLyricsAsync(trackId)
            .Fmap(x => x.Message.Body.Lyrics.LyricsBody);
}