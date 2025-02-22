using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NadekoBot.VotesApi.Services;

namespace NadekoBot.VotesApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TopGgController : ControllerBase
    {
        private readonly ILogger<TopGgController> _logger;
        private readonly IVotesCache _cache;

        public TopGgController(ILogger<TopGgController> logger, IVotesCache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        [HttpGet("new")]
        [Authorize(Policy = Policies.TopggAuth)]
        public async Task<IEnumerable<Vote>> New()
        {
            var votes = await _cache.GetNewTopGgVotesAsync();
            if(votes.Count > 0)
                _logger.LogInformation("Sending {NewTopggVotes} new topgg votes", votes.Count);
            
            return votes;
        }
    }
}