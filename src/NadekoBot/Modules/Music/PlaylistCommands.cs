#nullable disable
using LinqToDB;
using NadekoBot.Modules.Music.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Music;

public sealed partial class Music
{
    [Group]
    public sealed partial class PlaylistCommands : NadekoModule<IMusicService>
    {
        private static readonly SemaphoreSlim _playlistLock = new(1, 1);
        private readonly DbService _db;
        private readonly IBotCreds _creds;

        public PlaylistCommands(DbService db, IBotCreds creds)
        {
            _db = db;
            _creds = creds;
        }

        private async Task EnsureBotInVoiceChannelAsync(ulong voiceChannelId, IGuildUser botUser = null)
        {
            botUser ??= await ctx.Guild.GetCurrentUserAsync();
            await _voiceChannelLock.WaitAsync();
            try
            {
                if (botUser.VoiceChannel?.Id is null || !_service.TryGetMusicPlayer(ctx.Guild.Id, out _))
                    await _service.JoinVoiceChannelAsync(ctx.Guild.Id, voiceChannelId);
            }
            finally
            {
                _voiceChannelLock.Release();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Playlists([Leftover] int num = 1)
        {
            if (num <= 0)
                return;

            List<MusicPlaylist> playlists;

            await using (var uow = _db.GetDbContext())
            {
                playlists = uow.Set<MusicPlaylist>().GetPlaylistsOnPage(num);
            }

            var embed = CreateEmbed()
                        .WithAuthor(GetText(strs.playlists_page(num)), MUSIC_ICON_URL)
                        .WithDescription(string.Join("\n",
                            playlists.Select(r => GetText(strs.playlists(r.Id, r.Name, r.Author, r.Songs.Count)))))
                        .WithOkColor();

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task DeletePlaylist([Leftover] int id)
        {
            var success = false;
            try
            {
                await using var uow = _db.GetDbContext();
                var pl = uow.Set<MusicPlaylist>().FirstOrDefault(x => x.Id == id);

                if (pl is not null)
                {
                    if (_creds.IsOwner(ctx.User) || pl.AuthorId == ctx.User.Id)
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
        public async Task Save([Leftover] string name)
        {
            if (!_service.TryGetMusicPlayer(ctx.Guild.Id, out var mp))
            {
                await Response().Error(strs.no_player).SendAsync();
                return;
            }

            var songs = mp.GetQueuedTracks()
                          .Select(s => new PlaylistSong
                          {
                              Provider = s.Platform.ToString(),
                              ProviderType = (MusicType)s.Platform,
                              Title = s.Title,
                              Query = s.Url
                          })
                          .ToList();

            MusicPlaylist playlist;
            await using (var uow = _db.GetDbContext())
            {
                playlist = new()
                {
                    Name = name,
                    Author = ctx.User.Username,
                    AuthorId = ctx.User.Id,
                    Songs = songs.ToList()
                };
                uow.Set<MusicPlaylist>().Add(playlist);
                await uow.SaveChangesAsync();
            }

            await Response()
                  .Embed(CreateEmbed()
                         .WithOkColor()
                         .WithTitle(GetText(strs.playlist_saved))
                         .AddField(GetText(strs.name), name)
                         .AddField(GetText(strs.id), playlist.Id.ToString()))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Load([Leftover] int id)
        {
            // expensive action, 1 at a time
            await _playlistLock.WaitAsync();
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
                await EnsureBotInVoiceChannelAsync(voiceChannelId!.Value, botUser);

                if (botUser.VoiceChannel?.Id != voiceChannelId)
                {
                    await Response().Error(strs.not_with_bot_in_voice).SendAsync();
                    return;
                }

                var mp = await _service.GetOrCreateMusicPlayerAsync((ITextChannel)ctx.Channel);
                if (mp is null)
                {
                    await Response().Error(strs.no_player).SendAsync();
                    return;
                }

                MusicPlaylist mpl;
                await using (var uow = _db.GetDbContext())
                {
                    mpl = uow.Set<MusicPlaylist>().GetWithSongs(id);
                }

                if (mpl is null)
                {
                    await Response().Error(strs.playlist_id_not_found).SendAsync();
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

                await mp.EnqueueManyAsync(mpl.Songs.Select(x => (x.Query, (MusicPlatform)x.ProviderType)),
                    ctx.User.ToString());

                if (msg is not null)
                    await msg.ModifyAsync(m => m.Content = GetText(strs.playlist_queue_complete));
            }
            finally
            {
                _playlistLock.Release();
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