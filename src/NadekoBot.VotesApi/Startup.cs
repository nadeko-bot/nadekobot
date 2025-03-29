using System;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NadekoBot.GrpcVotesApi;

namespace NadekoBot.VotesApi
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
            => Configuration = configuration;


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            services.AddGrpcClient<VoteService.VoteServiceClient>(options =>
                {
                    options.Address = new Uri(Configuration["BotGrpcHost"]!);
                })
                .ConfigureChannel((sp, c) =>
                {
                    c.Credentials = ChannelCredentials.Insecure;
                    c.ServiceProvider = sp;
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