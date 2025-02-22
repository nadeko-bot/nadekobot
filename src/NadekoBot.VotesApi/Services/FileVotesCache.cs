using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MorseCode.ITask;

namespace NadekoBot.VotesApi.Services
{
    public class FileVotesCache : IVotesCache
    {
        // private const string STATS_FILE = "store/stats.json";
        private const string TOPGG_FILE = "store/topgg.json";
        private const string DISCORDS_FILE = "store/discords.json";

        private readonly SemaphoreSlim _locker = new SemaphoreSlim(1, 1);

        public FileVotesCache()
        {
            if (!Directory.Exists("store"))
                Directory.CreateDirectory("store");
            
            if(!File.Exists(TOPGG_FILE))
                File.WriteAllText(TOPGG_FILE, "[]");
            
            if(!File.Exists(DISCORDS_FILE))
                File.WriteAllText(DISCORDS_FILE, "[]");
        }

        public ITask AddNewTopggVote(string userId)
            => AddNewVote(TOPGG_FILE, userId);

        public ITask AddNewDiscordsVote(string userId)
            => AddNewVote(DISCORDS_FILE, userId);

        private async ITask AddNewVote(string file, string userId)
        {
            await _locker.WaitAsync();
            try
            {
                var votes = await GetVotesAsync(file);
                votes.Add(userId);
                await File.WriteAllTextAsync(file , JsonSerializer.Serialize(votes));
            }
            finally
            {
                _locker.Release();
            }
        }

        public async ITask<IList<Vote>> GetNewTopGgVotesAsync()
        {
            var votes = await EvictTopggVotes();
            return votes;
        }

        public async ITask<IList<Vote>> GetNewDiscordsVotesAsync()
        {
            var votes = await EvictDiscordsVotes();
            return votes;
        }

        private ITask<List<Vote>> EvictTopggVotes()
            => EvictVotes(TOPGG_FILE);

        private ITask<List<Vote>> EvictDiscordsVotes()
            => EvictVotes(DISCORDS_FILE);

        private async ITask<List<Vote>> EvictVotes(string file)
        {
            await _locker.WaitAsync();
            try
            {

                var ids = await GetVotesAsync(file);
                await File.WriteAllTextAsync(file, "[]");
                
                return ids?
                    .Select(x => (Ok: ulong.TryParse(x, out var r), Id: r))
                    .Where(x => x.Ok)
                    .Select(x => new Vote
                    {
                        UserId = x.Id
                    })
                    .ToList();
            }
            finally
            {
                _locker.Release();
            }
        }

        private async ITask<IList<string>> GetVotesAsync(string file)
        {
            await using var fs = File.Open(file, FileMode.Open);
            var votes = await JsonSerializer.DeserializeAsync<List<string>>(fs);
            return votes;
        }
    }
}