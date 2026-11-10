using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NadekoBot.Coordinator.Api;

namespace NadekoBot.Coordinator
{
    public class CoordStartup
    {
        public IConfiguration Configuration { get; }
        
        public CoordStartup(IConfiguration config)
            => Configuration = config;

        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureHttpJsonOptions(static o =>
                o.SerializerOptions.TypeInfoResolverChain.Insert(0, CoordJsonContext.Default));
            services.AddSingleton<CoordinatorRunner>();
            services.AddSingleton<IHostedService, CoordinatorRunner>(
                serviceProvider => serviceProvider.GetRequiredService<CoordinatorRunner>());
            services.AddSingleton<VoteRelay>();
            services.AddCoordApi();
        }
        
        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            CoordApiEndpoint api,
            IHostApplicationLifetime lifetime,
            IServer server)
        {
            lifetime.ApplicationStarted.Register(() => ShardProtocol.WarnIfNotLoopback(server.Features.Get<IServerAddressesFeature>(), api));

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCoordApiGate(api);

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapShardProtocol();

                if (api.Enabled)
                    endpoints.MapCoordApi();
            });
        }
    }
}
