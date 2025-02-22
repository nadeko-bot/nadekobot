using System.Collections.Generic;
using MorseCode.ITask;

namespace NadekoBot.VotesApi.Services
{
    public interface IVotesCache
    {
        ITask<IList<Vote>> GetNewTopGgVotesAsync();
        ITask<IList<Vote>> GetNewDiscordsVotesAsync();
        ITask AddNewTopggVote(string userId);
        ITask AddNewDiscordsVote(string userId);
    }
}