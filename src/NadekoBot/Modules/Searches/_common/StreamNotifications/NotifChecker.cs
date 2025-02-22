using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications;

public class NotifChecker
{
    public event Func<List<StreamData>, Task> OnStreamsOffline = _ => Task.CompletedTask;
    public event Func<List<StreamData>, Task> OnStreamsOnline = _ => Task.CompletedTask;

    private readonly IReadOnlyDictionary<FollowedStream.FType, Provider> _streamProviders;
    private readonly HashSet<(FollowedStream.FType, string)> _offlineBuffer;
    private readonly ConcurrentDictionary<StreamDataKey, StreamData?> _cache = new();

    public NotifChecker(
        IHttpClientFactory httpClientFactory,
        IBotCredsProvider credsProvider)
    {
        _streamProviders = new Dictionary<FollowedStream.FType, Provider>()
        {
            { FollowedStream.FType.Twitch, new TwitchHelixProvider(httpClientFactory, credsProvider) },
            { FollowedStream.FType.Picarto, new PicartoProvider(httpClientFactory) },
            { FollowedStream.FType.Trovo, new TrovoProvider(httpClientFactory, credsProvider) }
        };
        _offlineBuffer = new();
    }

    // gets all streams which have been failing for more than the provided timespan
    public IEnumerable<StreamDataKey> GetFailingStreams(TimeSpan duration, bool remove = false)
    {
        var toReturn = _streamProviders
                       .SelectMany(prov => prov.Value
                                               .FailingStreams
                                               .Where(fs => DateTime.UtcNow - fs.Value > duration)
                                               .Select(fs => new StreamDataKey(prov.Value.Platform, fs.Key)))
                       .ToList();

        if (remove)
        {
            foreach (var toBeRemoved in toReturn)
                _streamProviders[toBeRemoved.Type].ClearErrorsFor(toBeRemoved.Name);
        }

        return toReturn;
    }

    public Task RunAsync()
        => Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var allStreamData = GetAllData();

                    var oldStreamDataDict = allStreamData
                                            // group by type
                                            .GroupBy(entry => entry.Key.Type)
                                            .ToDictionary(entry => entry.Key,
                                                entry => entry.AsEnumerable()
                                                              .ToDictionary(x => x.Key.Name, x => x.Value));

                    var newStreamData = await oldStreamDataDict
                                              .Select(x =>
                                              {
                                                  // get all stream data for the streams of this type
                                                  if (_streamProviders.TryGetValue(x.Key,
                                                          out var provider))
                                                  {
                                                      return provider.GetStreamDataAsync(x.Value
                                                          .Select(entry => entry.Key)
                                                          .ToList());
                                                  }

                                                  // this means there's no provider for this stream data, (and there was before?)
                                                  return Task.FromResult<IReadOnlyCollection<StreamData>>(
                                                      new List<StreamData>());
                                              })
                                              .WhenAll();

                    var newlyOnline = new List<StreamData>();
                    var newlyOffline = new List<StreamData>();
                    // go through all new stream data, compare them with the old ones
                    foreach (var newData in newStreamData.SelectMany(x => x))
                    {
                        // update cached data
                        var key = newData.CreateKey();

                        // compare old data with new data
                        if (!oldStreamDataDict.TryGetValue(key.Type, out var typeDict)
                            || !typeDict.TryGetValue(key.Name, out var oldData)
                            || oldData is null)
                        {
                            AddLastData(key, newData, true);
                            continue;
                        }
                        
                        // fill with last known game in case it's empty
                        if (string.IsNullOrWhiteSpace(newData.Game))
                            newData.Game = oldData.Game;
                        
                        AddLastData(key, newData, true);

                        // if the stream is offline, we need to check if it was
                        // marked as offline once previously
                        // if it was, that means this is second time we're getting offline
                        // status for that stream -> notify subscribers
                        // Note: This is done because twitch api will sometimes return an offline status
                        //       shortly after the stream is already online, which causes duplicate notifications.
                        //       (stream is online -> stream is offline -> stream is online again (and stays online))
                        //       This offlineBuffer will make it so that the stream has to be marked as offline TWICE
                        //       before it sends an offline notification to the subscribers.
                        var streamId = (key.Type, key.Name);
                        if (!newData.IsLive && _offlineBuffer.Remove(streamId))
                            newlyOffline.Add(newData);
                        else if (newData.IsLive != oldData.IsLive)
                        {
                            if (newData.IsLive)
                            {
                                _offlineBuffer.Remove(streamId);
                                newlyOnline.Add(newData);
                            }
                            else
                            {
                                _offlineBuffer.Add(streamId);
                                // newlyOffline.Add(newData);
                            }
                        }
                    }

                    var tasks = new List<Task>
                    {
                        Task.Delay(30_000)
                    };

                    if (newlyOnline.Count > 0)
                        tasks.Add(OnStreamsOnline(newlyOnline));

                    if (newlyOffline.Count > 0)
                        tasks.Add(OnStreamsOffline(newlyOffline));

                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error getting stream notifications: {ErrorMessage}", ex.Message);
                }
            }
        });

    public bool AddLastData(StreamDataKey key, StreamData? data, bool replace)
    {
        if (replace)
        {
            _cache[key] = data;
            return true;
        }
        
        return _cache.TryAdd(key, data);
    }

    public void DeleteLastData(StreamDataKey key)
        => _cache.TryRemove(key, out _);

    public Dictionary<StreamDataKey, StreamData?> GetAllData()
        => _cache.ToDictionary(x => x.Key, x => x.Value);

    public async Task<StreamData?> GetStreamDataByUrlAsync(string url)
    {
        // loop through all providers and see which regex matches
        foreach (var (_, provider) in _streamProviders)
        {
            var isValid = await provider.IsValidUrl(url);
            if (!isValid)
                continue;
            // if it's not a valid url, try another provider
            var data = await provider.GetStreamDataByUrlAsync(url);
            return data;
        }

        // if no provider found, return null
        return null;
    }

    /// <summary>
    ///     Return currently available stream data, get new one if none available, and start tracking the stream.
    /// </summary>
    /// <param name="url">Url of the stream</param>
    /// <returns>Stream data, if any</returns>
    public async Task<StreamData?> TrackStreamByUrlAsync(string url)
    {
        var data = await GetStreamDataByUrlAsync(url);
        EnsureTracked(data);
        return data;
    }

    /// <summary>
    ///     Make sure a stream is tracked using its stream data.
    /// </summary>
    /// <param name="data">Data to try to track if not already tracked</param>
    /// <returns>Whether it's newly added</returns>
    private bool EnsureTracked(StreamData? data)
    {
        // something failed, don't add anything to cache
        if (data is null)
            return false;

        // if stream is found, add it to the cache for tracking only if it doesn't already exist
        // because stream will be checked and events will fire in a loop. We don't want to override old state
        return AddLastData(data.CreateKey(), data, false);
    }

    public void UntrackStreamByKey(in StreamDataKey key)
        => DeleteLastData(key);
}