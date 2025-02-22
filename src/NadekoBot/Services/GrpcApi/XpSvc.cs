using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.NadekoExpressions;
using NadekoBot.Modules.Utility;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.GrpcApi;

public class XpSvc : GrpcXp.GrpcXpBase, IGrpcSvc, INService
{
    private readonly XpService _xp;
    private readonly DiscordSocketClient _client;
    private readonly IUserService _duSvc;

    public XpSvc(XpService xp, DiscordSocketClient client, IUserService duSvc)
    {
        _xp = xp;
        _client = client;
        _duSvc = duSvc;
    }

    public ServerServiceDefinition Bind()
        => GrpcXp.BindService(this);

    public override async Task<GetXpSettingsReply> GetXpSettings(
        GetXpSettingsRequest request,
        ServerCallContext context)
    {
        await Task.Yield();

        var guild = _client.GetGuild(request.GuildId);

        if (guild is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Guild not found"));

        var excludedChannels = _xp.GetExcludedChannels(request.GuildId);
        var excludedRoles = _xp.GetExcludedRoles(request.GuildId);
        var isServerExcluded = _xp.IsServerExcluded(request.GuildId);

        var reply = new GetXpSettingsReply();

        reply.Exclusions.AddRange(excludedChannels
            .Select(x => new ExclItemReply()
            {
                Id = x,
                Type = "Channel",
                Name = guild.GetChannel(x)?.Name ?? "????"
            })
            .Concat(excludedRoles
                .Select(x => new ExclItemReply()
                {
                    Id = x,
                    Type = "Role",
                    Name = guild.GetRole(x)?.Name ?? "????"
                })));

        var settings = await _xp.GetFullXpSettingsFor(request.GuildId);
        var curRews = settings.CurrencyRewards;
        var roleRews = settings.RoleRewards;

        var rews = curRews.Select(x => new RewItemReply()
        {
            Level = x.Level,
            Type = "Currency",
            Value = x.Amount.ToString()
        });

        rews = rews.Concat(roleRews.Select(x => new RewItemReply()
            {
                Level = x.Level,
                Type = x.Remove ? "RemoveRole" : "AddRole",
                Value = guild.GetRole(x.RoleId)?.ToString() ?? x.RoleId.ToString()
            }))
            .OrderBy(x => x.Level);

        reply.Rewards.AddRange(rews);

        reply.ServerExcluded = isServerExcluded;

        return reply;
    }

    public override async Task<AddExclusionReply> AddExclusion(AddExclusionRequest request, ServerCallContext context)
    {
        await Task.Yield();

        var success = false;
        var guild = _client.GetGuild(request.GuildId);

        if (guild is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Guild not found"));

        if (request.Type == "Role")
        {
            if (guild.GetRole(request.Id) is null)
                return new()
                {
                    Success = false
                };

            success = await _xp.ToggleExcludeRoleAsync(request.GuildId, request.Id);
        }
        else if (request.Type == "Channel")
        {
            if (guild.GetTextChannel(request.Id) is null)
                return new()
                {
                    Success = false
                };

            success = await _xp.ToggleExcludeChannelAsync(request.GuildId, request.Id);
        }

        return new()
        {
            Success = success
        };
    }

    public override async Task<DeleteExclusionReply> DeleteExclusion(
        DeleteExclusionRequest request,
        ServerCallContext context)
    {
        var success = false;
        if (request.Type == "Role")
            success = await _xp.ToggleExcludeRoleAsync(request.GuildId, request.Id);
        else
            success = await _xp.ToggleExcludeChannelAsync(request.GuildId, request.Id);

        return new DeleteExclusionReply
        {
            Success = success
        };
    }

    public override async Task<AddRewardReply> AddReward(AddRewardRequest request, ServerCallContext context)
    {
        await Task.Yield();

        var success = false;
        var guild = _client.GetGuild(request.GuildId);

        if (guild is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Guild not found"));

        if (request.Type == "AddRole" || request.Type == "RemoveRole")
        {
            if (!ulong.TryParse(request.Value, out var rid))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid role id"));

            var role = guild.GetRole(rid);
            if (role is null)
                return new()
                {
                    Success = false
                };

            await _xp.SetRoleRewardAsync(request.GuildId, request.Level, rid, request.Type == "RemoveRole");
            success = true;
        }
        // else if (request.Type == "Currency")
        // {
        //     if (!int.TryParse(request.Value, out var amount))
        //         throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid amount"));
        //
        //     _xp.SetCurrencyReward(request.GuildId, request.Level, amount);
        //     success = true;
        // }

        return new()
        {
            Success = success
        };
    }

    public override async Task<DeleteRewardReply> DeleteReward(DeleteRewardRequest request, ServerCallContext context)
    {
        var success = false;

        if (request.Type == "AddRole" || request.Type == "RemoveRole")
        {
            await _xp.ResetRoleRewardAsync(request.GuildId, request.Level);
            success = true;
        }
        else if (request.Type == "Currency")
        {
            await _xp.SetCurrencyReward(request.GuildId, request.Level, 0);
            success = true;
        }

        return new()
        {
            Success = success
        };
    }

    public override async Task<ResetUserXpReply> ResetUserXp(ResetUserXpRequest request, ServerCallContext context)
    {
        await _xp.XpReset(request.GuildId, request.UserId);

        return new ResetUserXpReply
        {
            Success = true
        };
    }

    public override async Task<GetXpLbReply> GetXpLb(GetXpLbRequest request, ServerCallContext context)
    {
        if (request.Page < 1)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Page must be greater than or equal to 1"));

        var guild = _client.GetGuild(request.GuildId);

        if (guild is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Guild not found"));

        var data = await _xp.GetGuildUserXps(request.GuildId, request.Page - 1);
        var total = await _xp.GetGuildXpUsersCountAsync(request.GuildId);

        var reply = new GetXpLbReply
        {
            Total = total
        };

        var users = await data
            .Select(async x =>
            {
                var user = guild.GetUser(x.UserId);

                if (user is null)
                {
                    var du = await _duSvc.GetUserAsync(x.UserId);
                    if (du is null)
                        return new XpLbUserReply
                        {
                            UserId = x.UserId,
                            Avatar = string.Empty,
                            Username = string.Empty,
                            Xp = x.Xp,
                            Level = new LevelStats(x.Xp).Level
                        };

                    return new XpLbUserReply()
                    {
                        UserId = x.UserId,
                        Avatar = du.RealAvatarUrl()?.ToString() ?? string.Empty,
                        Username = du.ToString() ?? string.Empty,
                        Xp = x.Xp,
                        Level = new LevelStats(x.Xp).Level
                    };
                }

                return new XpLbUserReply
                {
                    UserId = x.UserId,
                    Avatar = user?.GetAvatarUrl() ?? string.Empty,
                    Username = user?.ToString() ?? string.Empty,
                    Xp = x.Xp,
                    Level = new LevelStats(x.Xp).Level
                };
            })
            .WhenAll();

        reply.Users.AddRange(users);

        return reply;
    }

    public override async Task<SetServerExclusionReply> SetServerExclusion(
        SetServerExclusionRequest request,
        ServerCallContext context)
    {
        var newValue = await _xp.ToggleExcludeServerAsync(request.GuildId);
        return new()
        {
            Success = newValue
        };
    }
}