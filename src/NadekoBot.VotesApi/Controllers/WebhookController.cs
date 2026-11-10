using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace NadekoBot.VotesApi.Controllers
{
    [ApiController]
    public class WebhookController(ILogger<WebhookController> logger, BotVoteClient bot)
        : ControllerBase
    {
        [HttpPost("/discordswebhook")]
        [Authorize(Policy = Policies.DiscordsAuth)]
        public async Task<IActionResult> DiscordsWebhook([FromBody] DiscordsVoteWebhookModel data)
        {
            if (!string.Equals(data.Type, "vote", StringComparison.Ordinal))
            {
                logger.LogInformation("Ignored a discords.com webhook of type {Type}", data.Type);
                return Ok();
            }

            logger.LogInformation("User {UserId} has voted for Bot {BotId} on {Platform}",
                data.User,
                data.Bot,
                "discords.com");

            return await SendAsync(data.User, "discords");
        }

        [HttpPost("/topggwebhook")]
        [Authorize(Policy = Policies.TopggAuth)]
        public async Task<IActionResult> TopggWebhook([FromBody] TopggVoteWebhookModel data)
        {
            // The "Send test" button on top.gg sends type "test" with the presser's user id.
            if (!string.Equals(data.Type, "upvote", StringComparison.Ordinal))
            {
                logger.LogInformation("Ignored a top.gg webhook of type {Type}", data.Type);
                return Ok();
            }

            logger.LogInformation("User {UserId} has voted for Bot {BotId} on {Platform}",
                data.User,
                data.Bot,
                "top.gg");

            return await SendAsync(data.User, "topgg");
        }

        [HttpPost("/discordbotlistwebhook")]
        [Authorize(Policy = Policies.DiscordbotlistAuth)]
        public async Task<IActionResult> DiscordbotlistWebhook([FromBody] DiscordbotlistVoteWebhookModel data)
        {
            logger.LogInformation("User {UserId} has voted for Bot on {Platform}",
                data.Id,
                "discordbotlist.com");

            return await SendAsync(data.Id, "discordbotlist");
        }

        private async Task<IActionResult> SendAsync(string userId, string type)
            => await bot.SendAsync(userId, type)
                ? Ok()
                : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
