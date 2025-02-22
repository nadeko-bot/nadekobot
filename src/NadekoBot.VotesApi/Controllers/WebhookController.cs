using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NadekoBot.VotesApi.Services;

namespace NadekoBot.VotesApi.Controllers
{
    [ApiController]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> _logger;
        private readonly IVotesCache _votesCache;

        public WebhookController(ILogger<WebhookController> logger, IVotesCache votesCache)
        {
            _logger = logger;
            _votesCache = votesCache;
        }
        
        [HttpPost("/discordswebhook")]
        [Authorize(Policy = Policies.DiscordsAuth)]
        public async Task<IActionResult> DiscordsWebhook([FromBody]DiscordsVoteWebhookModel data)
        {

            _logger.LogInformation("User {UserId} has voted for Bot {BotId} on {Platform}",
                data.User,
                data.Bot,
                "discords.com");

            await _votesCache.AddNewDiscordsVote(data.User);
            return Ok();
        }

        [HttpPost("/topggwebhook")]
        [Authorize(Policy = Policies.TopggAuth)]
        public async Task<IActionResult> TopggWebhook([FromBody] TopggVoteWebhookModel data)
        {
            _logger.LogInformation("User {UserId} has voted for Bot {BotId} on {Platform}",
                data.User,
                data.Bot,
                "top.gg");

            await _votesCache.AddNewTopggVote(data.User);
            return Ok();
        }
    }
}