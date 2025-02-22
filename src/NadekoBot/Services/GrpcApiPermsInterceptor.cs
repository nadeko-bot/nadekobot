using Grpc.Core;
using Grpc.Core.Interceptors;

namespace NadekoBot.GrpcApi;

public sealed partial class GrpcApiPermsInterceptor : Interceptor
{
    private const GuildPerm DEFAULT_PERMISSION = GuildPermission.Administrator;

    private readonly DiscordSocketClient _client;

    public GrpcApiPermsInterceptor(DiscordSocketClient client)
    {
        _client = client;
    }

    private async Task RequestHandler(ServerCallContext context)
    {
        try
        {
            var method = context.Method[(context.Method.LastIndexOf('/') + 1)..];

            // get metadata
            var metadata = context
                           .RequestHeaders
                           .ToDictionary(x => x.Key, x => x.Value);

            Log.Information("grpc | g: {GuildId} | u: {UserID} | cmd: {Method}",
                metadata.TryGetValue("guildid", out var gidString) ? gidString : "none",
                metadata.TryGetValue("userid", out var uidString) ? uidString : "none",
                method);


            // there always has to be a user who makes the call
            if (!metadata.ContainsKey("userid"))
                throw new RpcException(new(StatusCode.Unauthenticated, "userid has to be specified."));

            // get the method name without the service name

            // if the method is explicitly marked as not requiring auth
            if (_noAuthRequired.Contains(method))
                return;

            // otherwise the method requires auth, and if it requires auth then the guildid has to be specified
            if (string.IsNullOrWhiteSpace(gidString))
                throw new RpcException(new(StatusCode.Unauthenticated, "guildid has to be specified."));

            var userId = ulong.Parse(metadata["userid"]);
            var guildId = ulong.Parse(gidString);

            // check if the user has the required permission
            if (_perms.TryGetValue(method, out var perm))
            {
                await EnsureUserHasPermission(guildId, userId, perm);
            }
            else
            {
                // if not then use the default, which is Administrator permission
                await EnsureUserHasPermission(guildId, userId, DEFAULT_PERMISSION);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error thrown by {ContextMethod}", context.Method);
            throw;
        }
    }
    
    private async Task EnsureUserHasPermission(ulong guildId, ulong userId, GuildPerm perm)
    {
        IGuild guild = _client.GetGuild(guildId);
        var user = guild is null ? null : await guild.GetUserAsync(userId);

        if (user is null)
            throw new RpcException(new Status(StatusCode.NotFound, "User not found"));

        if (!user.GuildPermissions.Has(perm))
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"You need {perm} permission to use this method"));
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await RequestHandler(context);
        return await continuation(requestStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await RequestHandler(context);
        await continuation(requestStream, responseStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await RequestHandler(context);
        await continuation(request, responseStream, context);
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await RequestHandler(context);
        return await continuation(request, context);
    }
}