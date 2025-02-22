using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NadekoBot.VotesApi
{
    public class AuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "AUTHORIZATION_SCHEME";
        public const string DiscordsClaim = "DISCORDS_CLAIM";
        public const string TopggClaim = "TOPGG_CLAIM";

        private readonly IConfiguration _conf;

        public AuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration conf)
            : base(options, logger, encoder)
            => _conf = conf;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>();

            if (_conf[ConfKeys.DISCORDS_KEY].Trim() == Request.Headers["Authorization"].ToString().Trim())
                claims.Add(new(DiscordsClaim, "true"));

            if (_conf[ConfKeys.TOPGG_KEY] == Request.Headers["Authorization"].ToString().Trim())
                claims.Add(new Claim(TopggClaim, "true"));

            return Task.FromResult(AuthenticateResult.Success(new(new(new ClaimsIdentity(claims)), SchemeName)));
        }
    }
}