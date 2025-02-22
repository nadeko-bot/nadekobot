using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using NadekoBot.VotesApi.Services;

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
            services.AddSingleton<IVotesCache, FileVotesCache>();
            services.AddSwaggerGen(static c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "NadekoBot.VotesApi", Version = "v1" });
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
                    opts.AddPolicy(Policies.DiscordsAuth, static policy => policy.RequireClaim(AuthHandler.DiscordsClaim));
                    opts.AddPolicy(Policies.TopggAuth, static policy => policy.RequireClaim(AuthHandler.TopggClaim));
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(static c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "NadekoBot.VotesApi v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(static endpoints => { endpoints.MapControllers(); });
        }
    }
}