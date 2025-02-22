#nullable disable
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    [Group]
    public partial class Club : NadekoModule<IClubService>
    {
        private readonly XpService _xps;

        public Club(XpService xps)
            => _xps = xps;

        [Cmd]
        public async Task ClubTransfer([Leftover] IUser newOwner)
        {
            var result = _service.TransferClub(ctx.User, newOwner);

            if (!result.TryPickT0(out var club, out var error))
            {
                if (error == ClubTransferError.NotOwner)
                    await Response().Error(strs.club_owner_only).SendAsync();
                else
                    await Response().Error(strs.club_target_not_member).SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(
                          strs.club_transfered(
                              Format.Bold(club.Name),
                              Format.Bold(newOwner.ToString())
                          )
                      )
                      .SendAsync();
            }
        }

        [Cmd]
        public async Task ClubAdmin([Leftover] IUser toAdmin)
        {
            var result = await _service.ToggleAdminAsync(ctx.User, toAdmin);

            if (result == ToggleAdminResult.AddedAdmin)
                await Response().Confirm(strs.club_admin_add(Format.Bold(toAdmin.ToString()))).SendAsync();
            else if (result == ToggleAdminResult.RemovedAdmin)
                await Response().Confirm(strs.club_admin_remove(Format.Bold(toAdmin.ToString()))).SendAsync();
            else if (result == ToggleAdminResult.NotOwner)
                await Response().Error(strs.club_owner_only).SendAsync();
            else if (result == ToggleAdminResult.CantTargetThyself)
                await Response().Error(strs.club_admin_invalid_target).SendAsync();
            else if (result == ToggleAdminResult.TargetNotMember)
                await Response().Error(strs.club_target_not_member).SendAsync();
        }

        [Cmd]
        public async Task ClubCreate([Leftover] string clubName)
        {
            var result = await _service.CreateClubAsync(ctx.User, clubName);

            if (result == ClubCreateResult.NameTooLong)
            {
                await Response().Error(strs.club_name_too_long).SendAsync();
                return;
            }

            if (result == ClubCreateResult.NameTaken)
            {
                await Response().Error(strs.club_name_taken).SendAsync();
                return;
            }

            if (result == ClubCreateResult.InsufficientLevel)
            {
                await Response().Error(strs.club_create_insuff_lvl).SendAsync();
                return;
            }

            if (result == ClubCreateResult.AlreadyInAClub)
            {
                await Response().Error(strs.club_already_in).SendAsync();
                return;
            }

            await Response().Confirm(strs.club_created(Format.Bold(clubName))).SendAsync();
        }

        [Cmd]
        public async Task ClubIcon([Leftover] string url = null)
        {
            if ((!Uri.IsWellFormedUriString(url, UriKind.Absolute) && url is not null))
            {
                await Response().Error(strs.club_icon_url_format).SendAsync();
                return;
            }

            var result = await _service.SetClubIconAsync(ctx.User.Id, url);
            if (result == SetClubIconResult.Success)
                await Response().Confirm(strs.club_icon_set).SendAsync();
            else if (result == SetClubIconResult.NotOwner)
                await Response().Error(strs.club_owner_only).SendAsync();
            else if (result == SetClubIconResult.TooLarge)
                await Response().Error(strs.club_icon_too_large).SendAsync();
            else if (result == SetClubIconResult.InvalidFileType)
                await Response().Error(strs.club_icon_invalid_filetype).SendAsync();
        }

        private async Task InternalClubInfoAsync(ClubInfo club)
        {
            var lvl = new LevelStats(club.Xp);
            var allUsers = club.Members.OrderByDescending(x =>
                               {
                                   var l = new LevelStats(x.TotalXp).Level;
                                   if (club.OwnerId == x.Id)
                                       return int.MaxValue;
                                   if (x.IsClubAdmin)
                                       return (int.MaxValue / 2) + l;
                                   return l;
                               })
                               .ToList();

            var rank = await _service.GetClubRankAsync(club.Id);

            await Response()
                  .Paginated()
                  .Items(allUsers)
                  .PageSize(10)
                  .Page((users, _) =>
                  {
                      var embed = CreateEmbed()
                                  .WithOkColor()
                                  .WithTitle($"{club}")
                                  .WithDescription(GetText(strs.level_x(lvl.Level + $" ({club.Xp} xp)")))
                                  .AddField(GetText(strs.desc),
                                      string.IsNullOrWhiteSpace(club.Description) ? "-" : club.Description)
                                  .AddField(GetText(strs.rank), $"#{rank}", true)
                                  .AddField(GetText(strs.owner), club.Owner.ToString(), true)
                                  // .AddField(GetText(strs.level_req), club.MinimumLevelReq.ToString(), true)
                                  .AddField(GetText(strs.members),
                                      string.Join("\n",
                                          users
                                              .Select(x =>
                                              {
                                                  var l = new LevelStats(x.TotalXp);
                                                  var lvlStr = Format.Bold($" ⟪{l.Level}⟫");
                                                  if (club.OwnerId == x.Id)
                                                      return x + "🌟" + lvlStr;
                                                  if (x.IsClubAdmin)
                                                      return x + "⭐" + lvlStr;
                                                  return x + lvlStr;
                                              })));

                      if (Uri.IsWellFormedUriString(club.ImageUrl, UriKind.Absolute))
                          return embed.WithThumbnailUrl(club.ImageUrl);

                      return embed;
                  })
                  .SendAsync();
        }

        [Cmd]
        [Priority(1)]
        public async Task ClubInformation(IUser user = null)
        {
            user ??= ctx.User;
            var club = _service.GetClubByMember(user);
            if (club is null)
            {
                await Response().Error(strs.club_user_not_in_club(Format.Bold(user.ToString()))).SendAsync();
                return;
            }

            await InternalClubInfoAsync(club);
        }

        [Cmd]
        [Priority(0)]
        public async Task ClubInformation([Leftover] string clubName = null)
        {
            if (string.IsNullOrWhiteSpace(clubName))
            {
                await ClubInformation(ctx.User);
                return;
            }

            if (!_service.GetClubByName(clubName, out var club))
            {
                await Response().Error(strs.club_not_exists).SendAsync();
                return;
            }

            await InternalClubInfoAsync(club);
        }

        [Cmd]
        public Task ClubBans(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            var club = _service.GetClubWithBansAndApplications(ctx.User.Id);
            if (club is null)
                return Response().Error(strs.club_admin_perms).SendAsync();

            var bans = club.Bans.Select(x => x.User).ToArray();

            return Response()
                   .Paginated()
                   .Items(bans)
                   .PageSize(10)
                   .CurrentPage(page)
                   .Page((items, _) =>
                   {
                       var toShow = string.Join("\n", items.Select(x => x.ToString()));

                       return CreateEmbed()
                              .WithTitle(GetText(strs.club_bans_for(club.ToString())))
                              .WithDescription(toShow)
                              .WithOkColor();
                   })
                   .SendAsync();
        }

        [Cmd]
        public Task ClubApps(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            var club = _service.GetClubWithBansAndApplications(ctx.User.Id);
            if (club is null)
                return Response().Error(strs.club_admin_perms).SendAsync();

            var apps = club.Applicants.Select(x => x.User).ToArray();

            return Response()
                   .Paginated()
                   .Items(apps)
                   .PageSize(10)
                   .CurrentPage(page)
                   .Page((items, _) =>
                   {
                       var toShow = string.Join("\n", items.Select(x => x.ToString()));

                       return CreateEmbed()
                              .WithTitle(GetText(strs.club_apps_for(club.ToString())))
                              .WithDescription(toShow)
                              .WithOkColor();
                   })
                   .SendAsync();
        }

        [Cmd]
        public async Task ClubApply([Leftover] string clubName)
        {
            if (string.IsNullOrWhiteSpace(clubName))
                return;

            if (!_service.GetClubByName(clubName, out var club))
            {
                await Response().Error(strs.club_not_exists).SendAsync();
                return;
            }

            var result = _service.ApplyToClub(ctx.User, club);
            if (result == ClubApplyResult.Success)
                await Response().Confirm(strs.club_applied(Format.Bold(club.ToString()))).SendAsync();
            else if (result == ClubApplyResult.Banned)
                await Response().Error(strs.club_join_banned).SendAsync();
            else if (result == ClubApplyResult.AlreadyApplied)
                await Response().Error(strs.club_already_applied).SendAsync();
            else if (result == ClubApplyResult.AlreadyInAClub)
                await Response().Error(strs.club_already_in).SendAsync();
        }

        [Cmd]
        [Priority(1)]
        public Task ClubAccept(IUser user)
            => ClubAccept(user.ToString());

        [Cmd]
        [Priority(0)]
        public async Task ClubAccept([Leftover] string userName)
        {
            var result = _service.AcceptApplication(ctx.User.Id, userName, out var discordUser);
            if (result == ClubAcceptResult.Accepted)
                await Response().Confirm(strs.club_accepted(Format.Bold(discordUser.ToString()))).SendAsync();
            else if (result == ClubAcceptResult.NoSuchApplicant)
                await Response().Error(strs.club_accept_invalid_applicant).SendAsync();
            else if (result == ClubAcceptResult.NotOwnerOrAdmin)
                await Response().Error(strs.club_admin_perms).SendAsync();
        }

        [Cmd]
        [Priority(1)]
        public Task ClubReject(IUser user)
            => ClubReject(user.ToString());

        [Cmd]
        [Priority(0)]
        public async Task ClubReject([Leftover] string userName)
        {
            var result = _service.RejectApplication(ctx.User.Id, userName, out var discordUser);
            if (result == ClubDenyResult.Rejected)
                await Response().Confirm(strs.club_rejected(Format.Bold(discordUser.ToString()))).SendAsync();
            else if (result == ClubDenyResult.NoSuchApplicant)
                await Response().Error(strs.club_accept_invalid_applicant).SendAsync();
            else if (result == ClubDenyResult.NotOwnerOrAdmin)
                await Response().Error(strs.club_admin_perms).SendAsync();
        }

        [Cmd]
        public async Task ClubLeave()
        {
            var res = _service.LeaveClub(ctx.User);

            if (res == ClubLeaveResult.Success)
                await Response().Confirm(strs.club_left).SendAsync();
            else if (res == ClubLeaveResult.NotInAClub)
                await Response().Error(strs.club_not_in_a_club).SendAsync();
            else
                await Response().Error(strs.club_owner_cant_leave).SendAsync();
        }

        [Cmd]
        [Priority(1)]
        public Task ClubKick([Leftover] IUser user)
            => ClubKick(user.ToString());

        [Cmd]
        [Priority(0)]
        public Task ClubKick([Leftover] string userName)
        {
            var result = _service.Kick(ctx.User.Id, userName, out var club);
            if (result == ClubKickResult.Success)
            {
                return Response()
                       .Confirm(strs.club_user_kick(Format.Bold(userName),
                           Format.Bold(club.ToString())))
                       .SendAsync();
            }

            if (result == ClubKickResult.Hierarchy)
                return Response().Error(strs.club_kick_hierarchy).SendAsync();

            if (result == ClubKickResult.NotOwnerOrAdmin)
                return Response().Error(strs.club_admin_perms).SendAsync();

            return Response().Error(strs.club_target_not_member).SendAsync();
        }

        [Cmd]
        [Priority(1)]
        public Task ClubBan([Leftover] IUser user)
            => ClubBan(user.ToString());

        [Cmd]
        [Priority(0)]
        public Task ClubBan([Leftover] string userName)
        {
            var result = _service.Ban(ctx.User.Id, userName, out var club);
            if (result == ClubBanResult.Success)
            {
                return Response()
                       .Confirm(strs.club_user_banned(Format.Bold(userName),
                           Format.Bold(club.ToString())))
                       .SendAsync();
            }

            if (result == ClubBanResult.Unbannable)
                return Response().Error(strs.club_ban_fail_unbannable).SendAsync();

            if (result == ClubBanResult.WrongUser)
                return Response().Error(strs.club_ban_fail_user_not_found).SendAsync();

            return Response().Error(strs.club_admin_perms).SendAsync();
        }

        [Cmd]
        [Priority(1)]
        public Task ClubUnBan([Leftover] IUser user)
            => ClubUnBan(user.ToString());

        [Cmd]
        [Priority(0)]
        public Task ClubUnBan([Leftover] string userName)
        {
            var result = _service.UnBan(ctx.User.Id, userName, out var club);

            if (result == ClubUnbanResult.Success)
            {
                return Response()
                       .Confirm(strs.club_user_unbanned(Format.Bold(userName),
                           Format.Bold(club.ToString())))
                       .SendAsync();
            }

            if (result == ClubUnbanResult.WrongUser)
            {
                return Response().Error(strs.club_unban_fail_user_not_found).SendAsync();
            }

            return Response().Error(strs.club_admin_perms).SendAsync();
        }

        [Cmd]
        public async Task ClubDescription([Leftover] string desc = null)
        {
            if (_service.SetDescription(ctx.User.Id, desc))
            {
                desc = string.IsNullOrWhiteSpace(desc)
                    ? "-"
                    : desc;

                var eb = CreateEmbed()
                         .WithAuthor(ctx.User)
                         .WithTitle(GetText(strs.club_desc_update))
                         .WithOkColor()
                         .WithDescription(desc);

                await Response().Embed(eb).SendAsync();
            }
            else
            {
                await Response().Error(strs.club_desc_update_failed).SendAsync();
            }
        }

        [Cmd]
        public async Task ClubDisband()
        {
            if (_service.Disband(ctx.User.Id, out var club))
                await Response().Confirm(strs.club_disbanded(Format.Bold(club.Name))).SendAsync();
            else
                await Response().Error(strs.club_disband_error).SendAsync();
        }

        [Cmd]
        public Task ClubLeaderboard(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            var clubs = _service.GetClubLeaderboardPage(page);

            var embed = CreateEmbed().WithTitle(GetText(strs.club_leaderboard(page + 1))).WithOkColor();

            var i = page * 9;
            foreach (var club in clubs)
                embed.AddField($"#{++i} " + club, club.Xp + " xp");

            return Response().Embed(embed).SendAsync();
        }

        [Cmd]
        public async Task ClubRename([Leftover] string clubName)
        {
            var res = await _service.RenameClubAsync(ctx.User.Id, clubName);

            switch (res)
            {
                case ClubRenameResult.NameTooLong:
                    await Response().Error(strs.club_name_too_long).SendAsync();
                    return;
                case ClubRenameResult.Success:
                    {
                        var embed = CreateEmbed().WithTitle(GetText(strs.club_renamed(clubName))).WithOkColor();
                        await Response().Embed(embed).SendAsync();
                        return;
                    }
                case ClubRenameResult.NameTaken:
                    await Response().Error(strs.club_name_taken).SendAsync();
                    return;
                case ClubRenameResult.NotOwnerOrAdmin:
                    await Response().Error(strs.club_admin_perms).SendAsync();
                    return;
                default:
                    return;
            }
        }
    }
}