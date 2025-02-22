using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.GrpcApi;

public static class GrpcApiExtensions
{
    public static ulong GetUserId(this ServerCallContext context)
        => ulong.Parse(context.RequestHeaders.FirstOrDefault(x => x.Key == "userid")!.Value);
}

public sealed class OtherSvc : GrpcOther.GrpcOtherBase, IGrpcSvc, INService
{
    private readonly DiscordSocketClient _client;
    private readonly XpService _xp;
    private readonly ICurrencyService _cur;
    private readonly WaifuService _waifus;
    private readonly IStatsService _stats;
    private readonly CommandHandler _cmdHandler;

    public OtherSvc(
        DiscordSocketClient client,
        XpService xp,
        ICurrencyService cur,
        WaifuService waifus,
        IStatsService stats,
        CommandHandler cmdHandler)
    {
        _client = client;
        _xp = xp;
        _cur = cur;
        _waifus = waifus;
        _stats = stats;
        _cmdHandler = cmdHandler;
    }

    public ServerServiceDefinition Bind()
        => GrpcOther.BindService(this);

    [GrpcNoAuthRequired]
    public override Task<BotOnGuildReply> BotOnGuild(BotOnGuildRequest request, ServerCallContext context)
    {
        var guild = _client.GetGuild(request.GuildId);

        var reply = new BotOnGuildReply
        {
            Success = guild is not null
        };

        return Task.FromResult(reply);
    }

    public override Task<GetRolesReply> GetRoles(GetRolesRequest request, ServerCallContext context)
    {
        var g = _client.GetGuild(request.GuildId);
        var roles = g?.Roles;
        var reply = new GetRolesReply();
        reply.Roles.AddRange(roles?.Select(x => new RoleReply()
                             {
                                 Id = x.Id,
                                 Name = x.Name,
                                 Color = x.Color.ToString(),
                                 IconUrl = x.GetIconUrl() ?? string.Empty,
                             })
                             ?? new List<RoleReply>());

        return Task.FromResult(reply);
    }

    public override async Task<GetTextChannelsReply> GetTextChannels(
        GetTextChannelsRequest request,
        ServerCallContext context)
    {
        IGuild g = _client.GetGuild(request.GuildId);
        var reply = new GetTextChannelsReply();

        var chs = await g.GetTextChannelsAsync();

        reply.TextChannels.AddRange(chs.Select(x => new TextChannelReply()
        {
            Id = x.Id,
            Name = x.Name,
        }));

        return reply;
    }


    [GrpcNoAuthRequired]
    public override async Task<CurrencyLbReply> GetCurrencyLb(GetLbRequest request, ServerCallContext context)
    {
        var users = await _cur.GetTopRichest(_client.CurrentUser.Id, request.Page, request.PerPage);

        var reply = new CurrencyLbReply();
        var entries = users.Select(x =>
        {
            var user = _client.GetUser(x.UserId);
            return Task.FromResult(new CurrencyLbEntryReply()
            {
                Amount = x.CurrencyAmount,
                User = user?.ToString() ?? x.Username,
                UserId = x.UserId,
                Avatar = user?.RealAvatarUrl().ToString() ?? x.RealAvatarUrl()?.ToString()
            });
        });

        reply.Entries.AddRange(await entries.WhenAll());

        return reply;
    }

    [GrpcNoAuthRequired]
    public override async Task<XpLbReply> GetXpLb(GetLbRequest request, ServerCallContext context)
    {
        var users = await _xp.GetGlobalUserXps(request.Page);

        var reply = new XpLbReply();

        var entries = users.Select(x =>
        {
            var lvl = new LevelStats(x.TotalXp);

            return new XpLbEntryReply()
            {
                Level = lvl.Level,
                TotalXp = x.TotalXp,
                User = x.Username,
                UserId = x.UserId
            };
        });

        reply.Entries.AddRange(entries);

        return reply;
    }

    [GrpcNoAuthRequired]
    public override async Task<WaifuLbReply> GetWaifuLb(GetLbRequest request, ServerCallContext context)
    {
        var waifus = await _waifus.GetTopWaifusAtPage(request.Page, request.PerPage);

        var reply = new WaifuLbReply();
        reply.Entries.AddRange(waifus.Select(x => new WaifuLbEntry()
        {
            ClaimedBy = x.ClaimerName ?? string.Empty,
            IsMutual = x.ClaimerName == x.Affinity,
            Value = x.Price,
            User = x.WaifuName,
        }));
        return reply;
    }

    [GrpcNoAuthRequired]
    public override async Task GetShardStats(
        Empty request,
        IServerStreamWriter<ShardStatsReply> responseStream,
        ServerCallContext context)
    {
        while (true)
        {
            var stats = new ShardStatsReply()
            {
                Id = _client.ShardId,
                Commands = _stats.CommandsRan,
                Uptime = _stats.GetUptimeString(),
                Status = GetConnectionState(_client.ConnectionState),
                GuildCount = _client.Guilds.Count,
            };

            await responseStream.WriteAsync(stats);
            await Task.Delay(1000);
        }
    }

    [GrpcNoAuthRequired]
    public override async Task GetCommandFeed(
        Empty request,
        IServerStreamWriter<CommandFeedEntry> responseStream,
        ServerCallContext context)
    {
        var taskCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnCommandExecuted(IUserMessage userMessage, CommandInfo commandInfo)
        {
            try
            {
                responseStream.WriteAsync(new()
                {
                    Command = commandInfo.Name
                });
            }
            catch
            {
                _cmdHandler.CommandExecuted -= OnCommandExecuted;
                taskCompletion.TrySetResult(true);
            }

            return Task.CompletedTask;
        }

        _cmdHandler.CommandExecuted += OnCommandExecuted;

        await taskCompletion.Task;
    }

    private string GetConnectionState(ConnectionState clientConnectionState)
    {
        return clientConnectionState switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Connecting => "Connecting",
            _ => "Disconnected"
        };
    }

    public override async Task<GetServerInfoReply> GetServerInfo(ServerInfoRequest request, ServerCallContext context)
    {
        var info = await _stats.GetGuildInfoAsync(request.GuildId);

        var reply = new GetServerInfoReply()
        {
            Id = info.Id,
            Name = info.Name,
            IconUrl = info.IconUrl,
            OwnerId = info.OwnerId,
            OwnerName = info.Owner,
            TextChannels = info.TextChannels,
            VoiceChannels = info.VoiceChannels,
            MemberCount = info.MemberCount,
            CreatedAt = info.CreatedAt.Ticks,
        };

        reply.Features.AddRange(info.Features);
        reply.Emojis.AddRange(info.Emojis.Select(x => new EmojiReply()
        {
            Name = x.Name,
            Url = x.Url,
            Code = x.ToString()
        }));

        reply.Roles.AddRange(info.Roles.Select(x => new RoleReply()
        {
            Id = x.Id,
            Name = x.Name,
            IconUrl = x.GetIconUrl() ?? string.Empty,
            Color = x.Color.ToString()
        }));

        return reply;
    }
}