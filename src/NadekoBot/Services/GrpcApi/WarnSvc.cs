using Grpc.Core;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using Enum = System.Enum;

namespace NadekoBot.GrpcApi;

public sealed class WarnSvc : GrpcWarn.GrpcWarnBase, IGrpcSvc, INService
{
    private readonly UserPunishService _ups;
    private readonly DiscordSocketClient _client;

    public WarnSvc(UserPunishService ups, DiscordSocketClient client)
    {
        _ups = ups;
        _client = client;
    }

    public ServerServiceDefinition Bind()
        => GrpcWarn.BindService(this);

    public override async Task<WarnSettingsReply> GetWarnSettings(
        WarnSettingsRequest request,
        ServerCallContext context)
    {
        var list = await _ups.WarnPunishList(request.GuildId);

        var wsr = new WarnSettingsReply();

        (wsr.ExpiryDays, wsr.DeleteOnExpire) =  await _ups.GetWarnExpire(request.GuildId);

        wsr.Punishments.AddRange(list.Select(x => new WarnPunishment()
        {
            Action = x.Punishment.ToString(),
            Duration = x.Time,
            Threshold = x.Count,
            Role = x.RoleId is ulong rid
                ? _client.GetGuild(request.GuildId)?.GetRole(rid)?.Name ?? x.RoleId?.ToString() ?? string.Empty
                : string.Empty
        }));

        return wsr;
    }

    public override async Task<SetWarnExpiryReply> SetWarnExpiry(
        SetWarnExpiryRequest request,
        ServerCallContext context)
    {
        if (request.ExpiryDays > 366)
        {
            return new SetWarnExpiryReply()
            {
                Success = false
            };
        }

        await _ups.WarnExpireAsync(request.GuildId, request.ExpiryDays, request.DeleteOnExpire);

        return new SetWarnExpiryReply()
        {
            Success = true
        };
    }

    public override async Task<DeleteWarnpReply> DeleteWarnp(DeleteWarnpRequest request, ServerCallContext context)
    {
        var succ = await _ups.WarnPunishRemove(request.GuildId, request.Threshold);

        return new DeleteWarnpReply
        {
            Success = succ
        };
    }

    public override async Task<AddWarnpReply> AddWarnp(AddWarnpRequest request, ServerCallContext context)
    {
        if (request.Punishment.Threshold <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Threshold must be greater than 0"));

        var g = _client.GetGuild(request.GuildId);

        if (g is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Guild not found"));

        if (!Enum.TryParse<PunishmentAction>(request.Punishment.Action, out var action))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid action"));

        IRole? role = null;
        if (action == PunishmentAction.AddRole && ulong.TryParse(request.Punishment.Role, out var roleId))
        {
            role = g.GetRole(roleId);

            if (role is null)
                return new AddWarnpReply()
                {
                    Success = false
                };
            
            if(!ulong.TryParse(context.RequestHeaders.GetValue("userid"), out var userId))
                return new AddWarnpReply()
                {
                    Success = false
                };

            var user = await ((IGuild)g).GetUserAsync(userId);

            if (user is null)
                throw new RpcException(new Status(StatusCode.NotFound, "User not found"));

            var userMaxRole = user.GetRoles().MaxBy(x => x.Position)?.Position ?? 0;
            if (g.OwnerId != user.Id && userMaxRole <= role.Position)
            {
                return new AddWarnpReply()
                {
                    Success = false
                };
            }
        }

        var duration = TimeSpan.FromMinutes(request.Punishment.Duration);

        var succ = await _ups.WarnPunish(request.GuildId,
            request.Punishment.Threshold,
            action,
            duration,
            role
        );

        return new AddWarnpReply()
        {
            Success = succ
        };
    }

    public override async Task<GetLatestWarningsReply> GetLatestWarnings(
        GetLatestWarningsRequest request,
        ServerCallContext context)
    {
        var (latest, count) = await _ups.GetLatestWarnings(request.GuildId, request.Page);

        var reply = new GetLatestWarningsReply()
        {
            TotalCount = count
        };

        reply.Warnings.AddRange(latest.Select(MapWarningToGrpcWarning));

        return reply;
    }

    public override async Task<GetUserWarningsReply> GetUserWarnings(
        GetUserWarningsRequest request,
        ServerCallContext context)
    {
        IReadOnlyCollection<Db.Models.Warning> latest = [];
        var count = 0;
        if (ulong.TryParse(request.User, out var userId))
        {
            (latest, count) = await _ups.GetUserWarnings(request.GuildId, userId, request.Page);
        }
        else if (_client.GetGuild(request.GuildId)?.Users.FirstOrDefault(x => x.Username == request.User) is { } user)
        {
            (latest, count) = await _ups.GetUserWarnings(request.GuildId, user.Id, request.Page);
        }
        else
        {
        }

        var reply = new GetUserWarningsReply
        {
            TotalCount = count
        };

        reply.Warnings.AddRange(latest.Select(MapWarningToGrpcWarning));

        return reply;
    }

    private Warning MapWarningToGrpcWarning(Db.Models.Warning x)
    {
        return new Warning
        {
            Id = new kwum(x.Id).ToString(),
            Forgiven = x.Forgiven,
            ForgivenBy = x.ForgivenBy ?? string.Empty,
            Reason = x.Reason ?? string.Empty,
            Timestamp = x.DateAdded is { } da ? Nadeko.Common.Extensions.ToTimestamp(da) : 0,
            Weight = x.Weight,
            Moderator = x.Moderator ?? string.Empty,
            User = _client.GetUser(x.UserId)?.Username ?? x.UserId.ToString(),
            UserId = x.UserId
        };
    }

    public override async Task<ForgiveWarningReply> ForgiveWarning(
        ForgiveWarningRequest request,
        ServerCallContext context)
    {
        if (!kwum.TryParse(request.WarnId, out var wid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid warning ID"));

        var succ = await _ups.ForgiveWarning(request.GuildId, wid, request.ModName);

        return new ForgiveWarningReply
        {
            Success = succ
        };
    }

    public override async Task<ForgiveWarningReply> DeleteWarning(
        ForgiveWarningRequest request,
        ServerCallContext context)
    {
        if (!kwum.TryParse(request.WarnId, out var wid))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid warning ID"));

        var succ = await _ups.WarnDelete(request.GuildId, wid);

        return new ForgiveWarningReply
        {
            Success = succ
        };
    }
}