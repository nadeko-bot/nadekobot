#nullable disable

// public class YtTrackService : INService
// {
// private readonly IGoogleApiService _google;
// private readonly IHttpClientFactory httpClientFactory;
// private readonly DiscordSocketClient _client;
// private readonly DbService _db;
// private readonly ConcurrentDictionary<string, ConcurrentDictionary<ulong, List<YtFollowedChannel>>> followedChannels;
// private readonly ConcurrentDictionary<string, DateTime> _latestPublishes = new ConcurrentDictionary<string, DateTime>();
//
// public YtTrackService(IGoogleApiService google, IHttpClientFactory httpClientFactory, DiscordSocketClient client,
//     DbService db)
// {
//     this._google = google;
//     this.httpClientFactory = httpClientFactory;
//     this._client = client;
//     this._db = db;
//
//     if (_client.ShardId == 0)
//     {
//         _ = CheckLoop();
//     }
// }
//
// public async Task CheckLoop()
// {
//     while (true)
//     {
//         await Task.Delay(10000);
//         using (var http = httpClientFactory.CreateClient())
//         {
//             await followedChannels.Select(kvp => CheckChannel(kvp.Key, kvp.Value.SelectMany(x => x.Value).ToList())).WhenAll();
//         }
//     }
// }
//
// /// <summary>
// /// Checks the specified youtube channel, and sends a message to all provided
// /// </summary>
// /// <param name="youtubeChannelId">Id of the youtube channel</param>
// /// <param name="followedChannels">Where to post updates if there is a new update</param>
// private async Task CheckChannel(string youtubeChannelId, List<YtFollowedChannel> followedChannels)
// {
//     var latestVid = (await _google.GetLatestChannelVideosAsync(youtubeChannelId, 1))
//         .FirstOrDefault();
//     if (latestVid is null)
//     {
//         return;
//     }
//
//     if (_latestPublishes.TryGetValue(youtubeChannelId, out var latestPub) && latestPub >= latestVid.PublishedAt)
//     {
//         return;
//     }
//     _latestPublishes[youtubeChannelId] = latestVid.PublishedAt;
//
//     foreach (var chObj in followedChannels)
//     {
//         var gCh = _client.GetChannel(chObj.ChannelId);
//         if (gCh is ITextChannel ch)
//         {
//             var msg = latestVid.GetVideoUrl();
//             if (!string.IsNullOrWhiteSpace(chObj.UploadMessage))
//                 msg = chObj.UploadMessage + Environment.NewLine + msg;
//
//             await ch.SendMessageAsync(msg);
//         }
//     }
// }
//
// /// <summary>
// /// Starts posting updates on the specified discord channel when a new video is posted on the specified YouTube channel.
// /// </summary>
// /// <param name="guildId">Id of the discord guild</param>
// /// <param name="channelId">Id of the discord channel</param>
// /// <param name="ytChannelId">Id of the youtube channel</param>
// /// <param name="uploadMessage">Message to post when a new video is uploaded, along with video URL</param>
// /// <returns>Whether adding was successful</returns>
// public async Task<bool> ToggleChannelFollowAsync(ulong guildId, ulong channelId, string ytChannelId, string uploadMessage)
// {
//     // to to see if we can get a video from that channel
//     var vids = await _google.GetLatestChannelVideosAsync(ytChannelId, 1);
//     if (vids.Count == 0)
//         return false;
//
//     using(var uow = _db.GetDbContext())
//     {
//         var gc = uow.GuildConfigsForId(guildId, set => set.Include(x => x.YtFollowedChannels));
//
//         // see if this yt channel was already followed on this discord channel
//         var oldObj = gc.YtFollowedChannels
//             .FirstOrDefault(x => x.ChannelId == channelId && x.YtChannelId == ytChannelId);
//
//         if(oldObj is not null)
//         {
//             return false;
//         }
//
//         // can only add up to 10 tracked channels per server
//         if (gc.YtFollowedChannels.Count >= 10)
//         {
//             return false;
//         }
//
//         var obj = new YtFollowedChannel
//         {
//             ChannelId = channelId,
//             YtChannelId = ytChannelId,
//             UploadMessage = uploadMessage
//         };
//
//         // add to database
//         gc.YtFollowedChannels.Add(obj);
//
//         // add to the local cache:
//
//         // get follows on all guilds
//         var allGuildFollows = followedChannels.GetOrAdd(ytChannelId, new ConcurrentDictionary<ulong, List<YtFollowedChannel>>());
//         // add to this guild's follows
//         allGuildFollows.AddOrUpdate(guildId,
//             new List<YtFollowedChannel>(),
//             (key, old) =>
//             {
//                 old.Add(obj);
//                 return old;
//             });
//
//         await uow.SaveChangesAsync();
//     }
//
//     return true;
// }
// }