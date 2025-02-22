using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;
using OneOf;

namespace NadekoBot.Modules.Xp.Services;

public class ClubService : INService, IClubService
{
    private readonly DbService _db;
    private readonly IHttpClientFactory _httpFactory;

    public ClubService(DbService db, IHttpClientFactory httpFactory)
    {
        _db = db;
        _httpFactory = httpFactory;
    }

    public async Task<ClubCreateResult> CreateClubAsync(IUser user, string clubName)
    {
        if (!CheckClubName(clubName))
            return ClubCreateResult.NameTooLong;

        //must be lvl 5 and must not be in a club already

        await using var uow = _db.GetDbContext();
        var du = uow.GetOrCreateUser(user);
        var xp = new LevelStats(du.TotalXp);

        if (xp.Level < 5)
            return ClubCreateResult.InsufficientLevel;

        if (du.ClubId is not null)
            return ClubCreateResult.AlreadyInAClub;

        if (await uow.Set<ClubInfo>().AnyAsyncEF(x => x.Name == clubName))
            return ClubCreateResult.NameTaken;

        du.IsClubAdmin = true;
        du.Club = new()
        {
            Name = clubName,
            Owner = du
        };
        uow.Set<ClubInfo>().Add(du.Club);
        await uow.SaveChangesAsync();

        await uow.GetTable<ClubApplicants>()
                 .DeleteAsync(x => x.UserId == du.Id);

        return ClubCreateResult.Success;
    }

    public OneOf<ClubInfo, ClubTransferError> TransferClub(IUser from, IUser newOwner)
    {
        using var uow = _db.GetDbContext();
        var club = uow.Set<ClubInfo>().GetByOwner(from.Id);
        var newOwnerUser = uow.GetOrCreateUser(newOwner);

        if (club is null || club.Owner.UserId != from.Id)
            return ClubTransferError.NotOwner;

        if (!club.Members.Contains(newOwnerUser))
            return ClubTransferError.TargetNotMember;

        club.Owner.IsClubAdmin = true; // old owner will stay as admin
        newOwnerUser.IsClubAdmin = true;
        club.Owner = newOwnerUser;
        uow.SaveChanges();
        return club;
    }

    public async Task<ToggleAdminResult> ToggleAdminAsync(IUser owner, IUser toAdmin)
    {
        if (owner.Id == toAdmin.Id)
            return ToggleAdminResult.CantTargetThyself;

        await using var uow = _db.GetDbContext();
        var club = uow.Set<ClubInfo>().GetByOwner(owner.Id);
        var adminUser = uow.GetOrCreateUser(toAdmin);

        if (club is null)
            return ToggleAdminResult.NotOwner;

        if (!club.Members.Contains(adminUser))
            return ToggleAdminResult.TargetNotMember;

        var newState = adminUser.IsClubAdmin = !adminUser.IsClubAdmin;
        await uow.SaveChangesAsync();
        return newState ? ToggleAdminResult.AddedAdmin : ToggleAdminResult.RemovedAdmin;
    }

    public ClubInfo GetClubByMember(IUser user)
    {
        using var uow = _db.GetDbContext();
        var member = uow.Set<ClubInfo>().GetByMember(user.Id);
        return member;
    }

    public async Task<SetClubIconResult> SetClubIconAsync(ulong ownerUserId, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            using var http = _httpFactory.CreateClient();
            using var temp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!temp.IsImage())
                return SetClubIconResult.InvalidFileType;

            if (temp.GetContentLength() > 5.Megabytes())
                return SetClubIconResult.TooLarge;
        }

        await using var uow = _db.GetDbContext();
        var club = uow.Set<ClubInfo>().GetByOwner(ownerUserId);

        if (club is null)
            return SetClubIconResult.NotOwner;

        club.ImageUrl = url;
        await uow.SaveChangesAsync();

        return SetClubIconResult.Success;
    }

    public bool GetClubByName(string clubName, out ClubInfo club)
    {
        using var uow = _db.GetDbContext();
        club = uow.Set<ClubInfo>().GetByName(clubName);

        return club is not null;
    }

    public async Task<int> GetClubRankAsync(int clubId)
    {
        await using var uow = _db.GetDbContext();

        var rank = await uow.Clubs
                            .ToLinqToDBTable()
                            .Where(x => x.Xp > (uow.Clubs.First(c => c.Id == clubId).Xp))
                            .CountAsyncLinqToDB();

        return rank + 1;
    }

    public ClubApplyResult ApplyToClub(IUser user, ClubInfo club)
    {
        using var uow = _db.GetDbContext();
        var du = uow.GetOrCreateUser(user);
        uow.SaveChanges();

        //user banned or a member of a club, or already applied,
        // or doesn't min minumum level requirement, can't apply
        if (du.ClubId is not null)
            return ClubApplyResult.AlreadyInAClub;

        if (club.Bans.Any(x => x.UserId == du.Id))
            return ClubApplyResult.Banned;

        if (club.Applicants.Any(x => x.UserId == du.Id))
            return ClubApplyResult.AlreadyApplied;

        var app = new ClubApplicants
        {
            ClubId = club.Id,
            UserId = du.Id
        };

        uow.Set<ClubApplicants>().Add(app);
        uow.SaveChanges();
        return ClubApplyResult.Success;
    }


    public ClubAcceptResult AcceptApplication(ulong clubOwnerUserId, string userName, out DiscordUser? discordUser )
    {
        discordUser = null;
        using var uow = _db.GetDbContext();
        var club = uow.Set<ClubInfo>().GetByOwnerOrAdmin(clubOwnerUserId);
        if (club is null)
            return ClubAcceptResult.NotOwnerOrAdmin;

        var applicant =
            club.Applicants.FirstOrDefault(x => x.User.ToString().ToUpperInvariant() == userName.ToUpperInvariant());
        if (applicant is null)
            return ClubAcceptResult.NoSuchApplicant;

        applicant.User.Club = club;
        applicant.User.IsClubAdmin = false;
        club.Applicants.Remove(applicant);

        //remove that user's all other applications
        uow.Set<ClubApplicants>()
           .RemoveRange(uow.Set<ClubApplicants>().AsQueryable().Where(x => x.UserId == applicant.User.Id));

        discordUser = applicant.User;
        uow.SaveChanges();
        return ClubAcceptResult.Accepted;
    }

    public ClubDenyResult RejectApplication(ulong clubOwnerUserId, string userName, out DiscordUser? discordUser)
    {
        discordUser = null;
        using var uow = _db.GetDbContext();
        var club = uow.Set<ClubInfo>().GetByOwnerOrAdmin(clubOwnerUserId);
        if (club is null)
            return ClubDenyResult.NotOwnerOrAdmin;

        var applicant =
            club.Applicants.FirstOrDefault(x => x.User.ToString().ToUpperInvariant() == userName.ToUpperInvariant());
        if (applicant is null)
            return ClubDenyResult.NoSuchApplicant;

        club.Applicants.Remove(applicant);

        discordUser = applicant.User;
        uow.SaveChanges();
        return ClubDenyResult.Rejected;
    }

    public ClubInfo GetClubWithBansAndApplications(ulong ownerUserId)
    {
        using var uow = _db.GetDbContext();
        return uow.Set<ClubInfo>().GetByOwnerOrAdmin(ownerUserId);
    }

    public ClubLeaveResult LeaveClub(IUser user)
    {
        using var uow = _db.GetDbContext();
        var du = uow.GetOrCreateUser(user, x => x.Include(u => u.Club));
        if (du.Club is null)
            return ClubLeaveResult.NotInAClub;
        if (du.Club.OwnerId == du.Id)
            return ClubLeaveResult.OwnerCantLeave;

        du.Club = null;
        du.IsClubAdmin = false;
        uow.SaveChanges();
        return ClubLeaveResult.Success;
    }

    public bool SetDescription(ulong userId, string? desc)
    {
        using var uow = _db.GetDbContext();
        var club = uow.Set<ClubInfo>().GetByOwner(userId);
        if (club is null)
            return false;

        club.Description = desc?.TrimTo(150, true);
        uow.SaveChanges();

        return true;
    }

    public bool Disband(ulong userId, out ClubInfo club)
    {
        using var uow = _db.GetDbContext();
        club = uow.Set<ClubInfo>().GetByOwner(userId);
        if (club is null)
            return false;

        uow.Set<ClubInfo>().Remove(club);
        uow.SaveChanges();
        return true;
    }

    public ClubBanResult Ban(ulong bannerId, string userName, out ClubInfo club)
    {
        using var uow = _db.GetDbContext();
        club = uow.Set<ClubInfo>().GetByOwnerOrAdmin(bannerId);
        if (club is null)
            return ClubBanResult.NotOwnerOrAdmin;

        var usr = club.Members.FirstOrDefault(x => x.ToString().ToUpperInvariant() == userName.ToUpperInvariant())
                  ?? club.Applicants
                         .FirstOrDefault(x => x.User.ToString().ToUpperInvariant() == userName.ToUpperInvariant())
                         ?.User;
        if (usr is null)
            return ClubBanResult.WrongUser;

        if (club.OwnerId == usr.Id
            || (usr.IsClubAdmin && club.Owner.UserId != bannerId)) // can't ban the owner kek, whew
            return ClubBanResult.Unbannable;

        club.Bans.Add(new()
        {
            Club = club,
            User = usr
        });
        club.Members.Remove(usr);

        var app = club.Applicants.FirstOrDefault(x => x.UserId == usr.Id);
        if (app is not null)
            club.Applicants.Remove(app);

        uow.SaveChanges();

        return ClubBanResult.Success;
    }

    public ClubUnbanResult UnBan(ulong ownerUserId, string userName, out ClubInfo club)
    {
        using var uow = _db.GetDbContext();
        club = uow.Set<ClubInfo>().GetByOwnerOrAdmin(ownerUserId);
        if (club is null)
            return ClubUnbanResult.NotOwnerOrAdmin;

        var ban = club.Bans.FirstOrDefault(x => x.User.ToString().ToUpperInvariant() == userName.ToUpperInvariant());
        if (ban is null)
            return ClubUnbanResult.WrongUser;

        club.Bans.Remove(ban);
        uow.SaveChanges();

        return ClubUnbanResult.Success;
    }


    public ClubKickResult Kick(ulong kickerId, string userName, out ClubInfo club)
    {
        using var uow = _db.GetDbContext();
        club = uow.Set<ClubInfo>().GetByOwnerOrAdmin(kickerId);
        if (club is null)
            return ClubKickResult.NotOwnerOrAdmin;

        var usr = club.Members.FirstOrDefault(x => x.ToString().ToUpperInvariant() == userName.ToUpperInvariant());
        if (usr is null)
            return ClubKickResult.TargetNotAMember;

        if (club.OwnerId == usr.Id || (usr.IsClubAdmin && club.Owner.UserId != kickerId))
            return ClubKickResult.Hierarchy;

        club.Members.Remove(usr);
        var app = club.Applicants.FirstOrDefault(x => x.UserId == usr.Id);
        if (app is not null)
            club.Applicants.Remove(app);
        uow.SaveChanges();

        return ClubKickResult.Success;
    }

    public List<ClubInfo> GetClubLeaderboardPage(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        using var uow = _db.GetDbContext();
        return uow.Set<ClubInfo>().GetClubLeaderboardPage(page);
    }

    public async Task<ClubRenameResult> RenameClubAsync(ulong userId, string clubName)
    {
        if (!CheckClubName(clubName))
            return ClubRenameResult.NameTooLong;

        await using var uow = _db.GetDbContext();

        var club = uow.Set<ClubInfo>().GetByOwnerOrAdmin(userId);

        if (club is null)
            return ClubRenameResult.NotOwnerOrAdmin;

        if (await uow.Set<ClubInfo>().AnyAsyncEF(x => x.Name == clubName))
            return ClubRenameResult.NameTaken;

        club.Name = clubName;

        await uow.SaveChangesAsync();

        return ClubRenameResult.Success;
    }

    private static bool CheckClubName(string clubName)
    {
        return !(string.IsNullOrWhiteSpace(clubName) || clubName.Length > 20);
    }
}