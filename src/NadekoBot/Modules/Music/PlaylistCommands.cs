#nullable disable
using CommandLine;
using LinqToDB;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Modules.Music.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Music;

public sealed partial class Music
{
    [Group]
    public sealed partial class PlaylistCommands : NadekoModule<IMusicService>
    {
        private readonly DbService _db;
        private readonly IBotCreds _creds;
        private readonly BotPolicy _policy;

        public PlaylistCommands(DbService db, IBotCreds creds, BotPolicy policy)
        {
            _policy = policy;
            _db = db;
            _creds = creds;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public Task Playlists([Leftover] int page = 1)
            => ListPlaylistsInternalAsync(null, page);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public Task Playlists(IUser user, [Leftover] int page = 1)
            => ListPlaylistsInternalAsync(user.Id, page);

        private async Task ListPlaylistsInternalAsync(ulong? authorId, int page)
        {
            if (page <= 0)
                return;

            var total = await _service.CountPlaylistsAsync(authorId);
            if (total == 0)
            {
                await Response().Error(strs.no_playlists).SendAsync();
                return;
            }

            var lastPage = (total - 1) / MusicService.PLAYLISTS_PER_PAGE;
            await Response()
                .Paginated()
                .PageItems(num => _service.GetPlaylistsPageAsync(authorId, num))
                .TotalElements(total)
                .PageSize(MusicService.PLAYLISTS_PER_PAGE)
                .CurrentPage(Math.Min(page - 1, lastPage))
                .Page((playlists, num) => CreateEmbed()
                    .WithAuthor(GetText(strs.playlists_page(num + 1)), MUSIC_ICON_URL)
                    .WithDescription(playlists.Select(r => GetText(strs.playlists(r.Id, r.Name, r.Author, r.SongCount))).Join("\n"))
                    .WithOkColor())
                .SendAsync();
        }
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task PlaylistDelete([Leftover] int id)
        {
            var success = false;
            try
            {
                await using var uow = _db.GetDbContext();
                var pl = uow.Set<MusicPlaylist>().FirstOrDefault(x => x.Id == id);

                if (pl is not null)
                {
                    if (BotPolicy.IsRealOwner(_creds, ctx.Message) || pl.AuthorId == ctx.User.Id)
                    {
                        uow.Set<MusicPlaylist>().Remove(pl);
                        await uow.SaveChangesAsync();
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error deleting playlist");
            }

            if (!success)
                await Response().Error(strs.playlist_delete_fail).SendAsync();
            else
                await Response().Confirm(strs.playlist_deleted).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task PlaylistShow(int id, int page = 1)
        {
            if (page-- < 1)
                return;

            MusicPlaylist mpl;
            await using (var uow = _db.GetDbContext())
            {
                mpl = uow.Set<MusicPlaylist>().GetWithSongs(id);
            }

            await Response()
                .Paginated()
                .Items(mpl.Songs)
                .PageSize(20)
                .CurrentPage(page)
                .Page((items, _) =>
                {
                    var i = 0;
                    var str = string.Join("\n",
                        items
                            .Select(x => $"`{++i}.` [{x.Title.TrimTo(45)}]({x.Query}) `{x.Provider}`"));
                    return CreateEmbed().WithTitle($"\"{mpl.Name}\" by {mpl.Author}")
                        .WithOkColor()
                        .WithDescription(str);
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task PlaylistSave([Leftover] string name)
        {
            if (name.Length > 20)
            {
                await Response().Error(strs.playlist_name_too_long).SendAsync();
                return;
            }

            if (!_service.TryGetMusicPlayer(ctx.Guild.Id, out var mp))
            {
                await Response().Error(strs.no_player).SendAsync();
                return;
            }

            // A saved Local track would let any member play a file path from the host later.
            var songs = mp.GetQueuedTracks()
                .Where(s => s.Platform != MusicPlatform.Local)
                .Select(s => new PlaylistSong
                {
                    Provider = s.Platform.ToString(),
                    ProviderType = (MusicType)s.Platform,
                    Title = s.Title,
                    Query = s.Url
                })
                .ToList();

            // An empty playlist would use one of the member's saved playlist slots.
            if (songs.Count == 0)
            {
                await Response().Error(strs.track_not_found).SendAsync();
                return;
            }

            var playlistId = await _service.SavePlaylistAsync(name, ctx.User.Id, ctx.User.Username, songs);
            if (playlistId is not { } id)
            {
                await Response()
                    .Error(strs.playlist_limit(MusicService.MAX_PLAYLISTS_PER_USER, Format.Code($"{prefix}playlists {ctx.User.Id}")))
                    .SendAsync();
                return;
            }

            await Response()
                .Embed(CreateEmbed()
                    .WithOkColor()
                    .WithTitle(GetText(strs.playlist_saved))
                    .AddField(GetText(strs.name), name)
                    .AddField(GetText(strs.id), id.ToString()))
                .SendAsync();
        }

        public class PlaylistLoadOptions : INadekoCommandOptions
        {
            [Option("shuffle")]
            public bool Shuffled { get; set; } = false;

            public void NormalizeOptions()
            {
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<PlaylistLoadOptions>]
        public async Task PlaylistLoad(int id, params string[] args)
        {
            var opts = OptionsParser.ParseFrom(new PlaylistLoadOptions(), args).Item1;
            await _service.WaitPlaylistLoadAsync();
            try
            {
                var user = (IGuildUser)ctx.User;
                var voiceChannelId = user.VoiceChannel?.Id;

                if (voiceChannelId is null)
                {
                    await Response().Error(strs.must_be_in_voice).SendAsync();
                    return;
                }

                _ = ctx.Channel.TriggerTypingAsync();

                var botUser = await ctx.Guild.GetCurrentUserAsync();
                await _service.EnsureVoiceChannelAsync(ctx.Guild.Id, voiceChannelId!.Value);

                if (botUser.VoiceChannel?.Id != voiceChannelId)
                {
                    await Response().Error(strs.not_with_bot_in_voice).SendAsync();
                    return;
                }

                var mp = await _service.GetOrCreateMusicPlayerAsync((ITextChannel)ctx.Channel);
                _service.AttachProxy(ctx.Guild.Id);

                MusicPlaylist mpl;
                await using (var uow = _db.GetDbContext())
                {
                    mpl = uow.Set<MusicPlaylist>()
                        .AsNoTracking()
                        .GetWithSongs(id);
                }

                if (mpl is null)
                {
                    await Response().Error(strs.playlist_id_not_found).SendAsync();
                    return;
                }

                if (mp.IsQueueFull)
                {
                    await Response().Error(strs.queue_full(mp.MaxQueueSize)).SendAsync();
                    return;
                }

                IUserMessage msg = null;
                try
                {
                    msg = await Response()
                        .Pending(strs.attempting_to_queue(Format.Bold(mpl.Songs.Count.ToString())))
                        .SendAsync();
                }
                catch (Exception)
                {
                }

                IEnumerable<PlaylistSong> songs = opts.Shuffled
                    ? mpl.Songs.Shuffle()
                    : mpl.Songs;

                await mp.EnqueueManyAsync(
                    songs.Select(x => (x.Query, (MusicPlatform)x.ProviderType)),
                    ctx.User.ToString(),
                    _policy.CanReachLocalNetwork(_creds, ctx.Message));

                if (msg is not null)
                {
                    try
                    {
                        await msg.ModifyAsync(m => m.Embed = CreateEmbed()
                            .WithOkColor()
                            .WithDescription(GetText(strs.playlist_queue_complete))
                            .Build());
                    }
                    catch (HttpException ex)
                    {
                        // The songs are queued; the message may be gone.
                        Log.Debug("Unable to edit the playlist load message: {ErrorMessage}", ex.Message);
                    }
                }
            }
            finally
            {
                _service.ExitPlaylistLoad();
            }
        }

        [Cmd]
        [OwnerOnly]
        public async Task DeletePlaylists()
        {
            await using var uow = _db.GetDbContext();
            await uow.Set<MusicPlaylist>().DeleteAsync();
            await uow.SaveChangesAsync();
        }
    }
}