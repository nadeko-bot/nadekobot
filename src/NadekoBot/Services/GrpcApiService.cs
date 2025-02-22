using Grpc.Core;
using Grpc.Core.Interceptors;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.GrpcApi;

public class GrpcApiService : INService, IReadyExecutor
{
    private Server? _app;

    private readonly DiscordSocketClient _client;
    private readonly IEnumerable<IGrpcSvc> _svcs;
    private readonly IBotCredsProvider _creds;

    public GrpcApiService(
        DiscordSocketClient client,
        IEnumerable<IGrpcSvc> svcs,
        IBotCredsProvider creds)
    {
        _client = client;
        _svcs = svcs;
        _creds = creds;
    }

    public Task OnReadyAsync()
    {
        var creds = _creds.GetCreds();
        if (creds.GrpcApi is null || !creds.GrpcApi.Enabled)
            return Task.CompletedTask;

        try
        {
            var host = creds.GrpcApi.Host;
            var port = creds.GrpcApi.Port + _client.ShardId;

            var interceptor = new GrpcApiPermsInterceptor(_client);

            var serverCreds = ServerCredentials.Insecure;

            if (creds.GrpcApi is
                {
                    CertPrivateKey: not null and not "",
                    CertChain: not null and not ""
                } cert)
            {
                serverCreds = new SslServerCredentials(
                    new[] { new KeyCertificatePair(cert.CertChain, cert.CertPrivateKey) });
            }
            
            _app = new()
            {
                Ports =
                {
                    new(host, port, serverCreds),
                }
            };

            foreach (var svc in _svcs)
            {
                _app.Services.Add(svc.Bind().Intercept(interceptor));
            }

            _app.Start();

            Log.Information("Grpc Api Server started on port {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error starting Grpc Api Server");
            _app?.ShutdownAsync().GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }
}