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

        var reply = new GetXpSettingsReply();

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

        return reply;
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
    
    /// <summary>
    /// Gets XP information for a specific user in a guild
    /// </summary>
    public override async Task<GetUserXpReply> GetUserXp(
        GetUserXpRequest request,
        ServerCallContext context)
    {
        var guild = _client.GetGuild(request.GuildId);
        
        if (guild is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Guild not found"));
            
        var user = guild.GetUser(request.UserId);
        
        if (user is null)
            throw new RpcException(new Status(StatusCode.NotFound, "User not found"));
            
        var reply = new GetUserXpReply();
        
        // Get user stats from the XP service
        var stats = await _xp.GetUserStatsAsync(user);
        var levelStats = stats.Guild;
        
        // Get user's rank in guild
        var guildRank = stats.GuildRanking;
        
        // Fill the response with user XP data
        reply.Xp = levelStats.LevelXp;
        reply.RequiredXp = levelStats.RequiredXp;
        reply.Level = levelStats.Level;
        reply.Rank = guildRank;
        
        // Add club information if available
        if (stats.User.Club is not null)
        {
            reply.Club = stats.User.Club.ToString();
            reply.ClubIcon = stats.User.Club.ImageUrl ?? string.Empty;
        }
        else
        {
            reply.Club = string.Empty;
            reply.ClubIcon = string.Empty;
        }
        
        return reply;
    }
    
}