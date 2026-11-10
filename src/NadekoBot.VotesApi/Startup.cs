using System;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.VotesApi
{
    public class Startup
    {
        private const string DEFAULT_COORDINATOR_URL = "http://localhost:3442";

        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
            => Configuration = configuration;


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            var url = Configuration[ConfKeys.COORDINATOR_URL];
            if (!Uri.TryCreate(string.IsNullOrWhiteSpace(url) ? DEFAULT_COORDINATOR_URL : url, UriKind.Absolute, out var coordinatorUrl)
                || !string.Equals(coordinatorUrl.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
                throw new InvalidOperationException($"{ConfKeys.COORDINATOR_URL} must be the http url of the coordinator, like {DEFAULT_COORDINATOR_URL}");

            // The coordinator's port speaks only HTTP/2, without TLS, and only on loopback.
            services.AddHttpClient<BotVoteClient>(c =>
                {
                    c.BaseAddress = coordinatorUrl;
                    c.DefaultRequestVersion = HttpVersion.Version20;
                    c.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                    c.Timeout = TimeSpan.FromSeconds(15);
                })
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(5)
                });

            services
                .AddAuthentication(opts =>
                {
                    opts.DefaultScheme = AuthHandler.SchemeName;
                    opts.AddScheme<AuthHandler>(AuthHandler.SchemeName, AuthHandler.SchemeName);
                });

            services
                .AddAuthorization(static opts =>
                {
                    opts.DefaultPolicy = new AuthorizationPolicyBuilder(AuthHandler.SchemeName)
                        .RequireAssertion(static _ => false)
                        .Build();
                    opts.AddPolicy(Policies.DiscordsAuth,
                        static policy => policy.RequireClaim(AuthHandler.DiscordsClaim));
                    opts.AddPolicy(Policies.TopggAuth,
                        static policy => policy.RequireClaim(AuthHandler.TopggClaim));
                    opts.AddPolicy(Policies.DiscordbotlistAuth,
                        static policy => policy.RequireClaim(AuthHandler.DiscordbotlistClaim));
                });

            services.AddCors(x => x.AddDefaultPolicy(cpb =>
                cpb.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(static endpoints => { endpoints.MapControllers(); });
        }
    }
}