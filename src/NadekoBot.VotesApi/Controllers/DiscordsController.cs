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
    public class DiscordsController : ControllerBase
    {
        private readonly ILogger<DiscordsController> _logger;
        private readonly IVotesCache _cache;

        public DiscordsController(ILogger<DiscordsController> logger, IVotesCache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        [HttpGet("new")]
        [Authorize(Policy = Policies.DiscordsAuth)]
        public async Task<IEnumerable<Vote>> New()
        {
            var votes = await _cache.GetNewDiscordsVotesAsync();
            if(votes.Count > 0)
                _logger.LogInformation("Sending {NewDiscordsVotes} new discords votes", votes.Count);
            return votes;
        }
    }
}