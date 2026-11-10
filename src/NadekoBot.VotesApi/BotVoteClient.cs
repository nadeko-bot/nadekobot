using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NadekoBot.VotesApi
{
    public sealed record BotVote(string UserId, string Type);

    // Gives each vote to NadekoBot.Coordinator, where shard 0 takes it.
    public sealed class BotVoteClient(HttpClient http, ILogger<BotVoteClient> logger)
    {
        // False tells the vote site to send the webhook again later.
        public async Task<bool> SendAsync(string userId, string type)
        {
            try
            {
                using var res = await http.PostAsJsonAsync("coord/votes", new BotVote(userId, type));
                if (res.IsSuccessStatusCode)
                    return true;

                // The coordinator would refuse the same vote on each retry.
                if (res.StatusCode == HttpStatusCode.BadRequest)
                {
                    logger.LogWarning("The coordinator refused the vote of {UserId} on {Platform} as not valid",
                        userId,
                        type);
                    return true;
                }

                logger.LogWarning("The coordinator refused the vote of {UserId} on {Platform}: {Status}",
                    userId,
                    type,
                    (int)res.StatusCode);
                return false;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "The coordinator did not answer the vote of {UserId} on {Platform}", userId, type);
                return false;
            }
            catch (TaskCanceledException ex)
            {
                logger.LogWarning(ex, "The coordinator did not answer the vote of {UserId} on {Platform} in time", userId, type);
                return false;
            }
        }
    }
}
