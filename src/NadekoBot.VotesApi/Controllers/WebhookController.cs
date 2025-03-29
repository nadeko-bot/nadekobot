using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NadekoBot.GrpcVotesApi;

namespace NadekoBot.VotesApi.Controllers
{
    [ApiController]
    public class WebhookController(ILogger<WebhookController> logger, VoteService.VoteServiceClient client)
        : ControllerBase
    {
        [HttpPost("/discordswebhook")]
        [Authorize(Policy = Policies.DiscordsAuth)]
        public async Task<IActionResult> DiscordsWebhook([FromBody] DiscordsVoteWebhookModel data)
        {
            if ((data.Type?.Contains("vote") ?? false) == false)
                return Ok();

            logger.LogInformation("User {UserId} has voted for Bot {BotId} on {Platform}",
                data.User,
                data.Bot,
                "discords.com");

            await client.VoteReceivedAsync(new GrpcVoteData()
            {
                Type = VoteType.Discords,
                UserId = data.User,
            });

            return Ok();
        }

        [HttpPost("/topggwebhook")]
        [Authorize(Policy = Policies.TopggAuth)]
        public async Task<IActionResult> TopggWebhook([FromBody] TopggVoteWebhookModel data)
        {
            logger.LogInformation("User {UserId} has voted for Bot {BotId} on {Platform}",
                data.User,
                data.Bot,
                "top.gg");

            await client.VoteReceivedAsync(new GrpcVoteData()
            {
                Type = VoteType.Topgg,
                UserId = data.User,
            });

            return Ok();
        }

        [HttpPost("/discordbotlistwebhook")]
        [Authorize(Policy = Policies.DiscordbotlistAuth)]
        public async Task<IActionResult> DiscordbotlistWebhook([FromBody] DiscordbotlistVoteWebhookModel data)
        {
            logger.LogInformation("User {UserId} has voted for Bot on {Platform}",
                data.Id,
                "discordbotlist.com");

            await client.VoteReceivedAsync(new GrpcVoteData()
            {
                Type = VoteType.Discordbotlist,
                UserId = data.Id,
            });

            return Ok();
        }
    }
}