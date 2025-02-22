using NadekoBot.Db.Models;
using OneOf;

namespace NadekoBot.Modules.Xp.Services;

public interface IClubService
{
    Task<ClubCreateResult> CreateClubAsync(IUser user, string clubName);
    OneOf<ClubInfo,ClubTransferError> TransferClub(IUser from, IUser newOwner);
    Task<ToggleAdminResult> ToggleAdminAsync(IUser owner, IUser toAdmin);
    ClubInfo? GetClubByMember(IUser user);
    Task<SetClubIconResult> SetClubIconAsync(ulong ownerUserId, string? url);
    bool GetClubByName(string clubName, out ClubInfo club);
    ClubApplyResult ApplyToClub(IUser user, ClubInfo club);
    ClubAcceptResult AcceptApplication(ulong clubOwnerUserId, string userName, out DiscordUser? discordUser);
    ClubDenyResult RejectApplication(ulong clubOwnerUserId, string userName, out DiscordUser? discordUser);
    ClubInfo? GetClubWithBansAndApplications(ulong ownerUserId);
    ClubLeaveResult LeaveClub(IUser user);
    bool SetDescription(ulong userId, string? desc);
    bool Disband(ulong userId, out ClubInfo club);
    ClubBanResult Ban(ulong bannerId, string userName, out ClubInfo club);
    ClubUnbanResult UnBan(ulong ownerUserId, string userName, out ClubInfo club);
    ClubKickResult Kick(ulong kickerId, string userName, out ClubInfo club);
    List<ClubInfo> GetClubLeaderboardPage(int page);
    Task<ClubRenameResult> RenameClubAsync(ulong userId, string clubName);
    Task<int> GetClubRankAsync(int clubId);
}

public enum ClubApplyResult
{
    Success,
    AlreadyInAClub,
    Banned,
    AlreadyApplied
}