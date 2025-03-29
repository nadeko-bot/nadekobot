using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
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
        public const string DiscordbotlistClaim = "DISCORDBOTLIST_CLAIM";

        private readonly IConfiguration _conf;

        public AuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration conf)
            : base(options, logger, encoder)
            => _conf = conf;

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                return AuthenticateResult.Fail("Authorization header missing");
            }

            var authToken = authHeader.ToString().Trim();
            if (string.IsNullOrWhiteSpace(authToken))
            {
                return AuthenticateResult.Fail("Authorization token empty");
            }

            var claims = new List<Claim>();

            var discsKey = _conf[ConfKeys.DISCORDS_KEY]?.Trim();
            var topggKey = _conf[ConfKeys.TOPGG_KEY]?.Trim();
            var dblKey = _conf[ConfKeys.DISCORDBOTLIST_KEY]?.Trim();

            if (!string.IsNullOrWhiteSpace(discsKey)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(discsKey),
                    Encoding.UTF8.GetBytes(authToken)))
            {
                claims.Add(new Claim(DiscordsClaim, "true"));
            }

            if (!string.IsNullOrWhiteSpace(topggKey)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(topggKey),
                    Encoding.UTF8.GetBytes(authToken)))
            {
                claims.Add(new Claim(TopggClaim, "true"));
            }

            if (!string.IsNullOrWhiteSpace(dblKey)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(dblKey),
                    Encoding.UTF8.GetBytes(authToken)))
            {
                claims.Add(new Claim(DiscordbotlistClaim, "true"));
            }

            if (claims.Count == 0)
            {
                return AuthenticateResult.Fail("Invalid authorization token");
            }

            return AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims)),
                    SchemeName));
        }
    }
}