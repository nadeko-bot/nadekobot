#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Db.Models;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Administration.Services;

public class UserPunishService : INService, IReadyExecutor
{
    private readonly MuteService _mute;
    private readonly DbService _db;
    private readonly BlacklistService _blacklistService;
    private readonly BotConfigService _bcs;
    private readonly DiscordSocketClient _client;
    private readonly IReplacementService _repSvc;
    private readonly TempRoleService _tempRoleService;

    public event Func<Warning, Task> OnUserWarned = static delegate { return Task.CompletedTask; };

    public UserPunishService(
        MuteService mute,
        DbService db,
        BlacklistService blacklistService,
        BotConfigService bcs,
        DiscordSocketClient client,
        IReplacementService repSvc,
        TempRoleService tempRoleService)
    {
        _mute = mute;
        _db = db;
        _blacklistService = blacklistService;
        _bcs = bcs;
        _client = client;
        _repSvc = repSvc;
        _tempRoleService = tempRoleService;
    }

    public async Task OnReadyAsync()
    {
        if (_client.ShardId != 0)
            return;

        using var expiryTimer = new PeriodicTimer(TimeSpan.FromHours(12));
        do
        {
            try
            {
                await CheckAllWarnExpiresAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error while checking for warn expiries: {ErrorMessage}", ex.Message);
            }
        } while (await expiryTimer.WaitForNextTickAsync());
    }

    public async Task<WarningPunishment> Warn(
        IGuild guild,
        ulong userId,
        IUser mod,
        long weight,
        string reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);

        var modName = mod.ToString();

        if (string.IsNullOrWhiteSpace(reason))
            reason = "-";

        var guildId = guild.Id;

        var warn = new Warning
        {
            UserId = userId,
            GuildId = guildId,
            Forgiven = false,
            Reason = reason,
            Moderator = modName,
            Weight = weight
        };

        long previousCount;
        var ps = await WarnPunishList(guildId);
        await using (var uow = _db.GetDbContext())
        {
            previousCount = uow.GetTable<Warning>()
                               .Where(w => w.GuildId == guildId && w.UserId == userId && !w.Forgiven)
                               .Sum(x => x.Weight);

            await uow.GetTable<Warning>()
                     .InsertAsync(() => new()
                     {
                         UserId = userId,
                         GuildId = guildId,
                         Forgiven = false,
                         Reason = reason,
                         Moderator = modName,
                         Weight = weight,
                         DateAdded = DateTime.UtcNow,
                     });

            await uow.SaveChangesAsync();
        }

        _ = OnUserWarned(warn);

        var totalCount = previousCount + weight;

        var p = ps.Where(x => x.Count > previousCount && x.Count <= totalCount)
                  .MaxBy(x => x.Count);

        if (p is not null)
        {
            var user = await guild.GetUserAsync(userId);
            if (user is null)
                return null;

            await ApplyPunishment(guild, user, mod, p.Punishment, p.Time, p.RoleId, "Warned too many times.");
            return p;
        }

        return null;
    }

    public async Task ApplyPunishment(
        IGuild guild,
        IGuildUser user,
        IUser mod,
        PunishmentAction p,
        int minutes,
        ulong? roleId,
        string reason)
    {
        if (!await CheckPermission(guild, p))
            return;

        int banPrune;
        switch (p)
        {
            case PunishmentAction.Mute:
                if (minutes == 0)
                    await _mute.MuteUser(user, mod, reason: reason);
                else
                    await _mute.TimedMute(user, mod, TimeSpan.FromMinutes(minutes), reason: reason);
                break;
            case PunishmentAction.VoiceMute:
                if (minutes == 0)
                    await _mute.MuteUser(user, mod, MuteType.Voice, reason);
                else
                    await _mute.TimedMute(user, mod, TimeSpan.FromMinutes(minutes), MuteType.Voice, reason);
                break;
            case PunishmentAction.ChatMute:
                if (minutes == 0)
                    await _mute.MuteUser(user, mod, MuteType.Chat, reason);
                else
                    await _mute.TimedMute(user, mod, TimeSpan.FromMinutes(minutes), MuteType.Chat, reason);
                break;
            case PunishmentAction.Kick:
                await user.KickAsync(reason);
                break;
            case PunishmentAction.Ban:
                banPrune = await GetBanPruneAsync(user.GuildId) ?? 7;
                if (minutes == 0)
                    await guild.AddBanAsync(user, reason: reason, pruneDays: banPrune);
                else
                    await _mute.TimedBan(user.Guild, user.Id, TimeSpan.FromMinutes(minutes), reason, banPrune);
                break;
            case PunishmentAction.Softban:
                banPrune = await GetBanPruneAsync(user.GuildId) ?? 7;
                await guild.AddBanAsync(user, banPrune, $"Softban | {reason}");
                try
                {
                    await guild.RemoveBanAsync(user);
                }
                catch
                {
                    await guild.RemoveBanAsync(user);
                }

                break;
            case PunishmentAction.RemoveRoles:
                await user.RemoveRolesAsync(user.GetRoles().Where(x => !x.IsManaged && x != x.Guild.EveryoneRole));
                break;
            case PunishmentAction.AddRole:
                if (roleId is null)
                    return;
                var role = guild.GetRole(roleId.Value);
                if (role is not null)
                {
                    await user.AddRoleAsync(role);
                    if (minutes != 0)
                    {
                        await _tempRoleService.AddTempRoleAsync(guild.Id,
                            role.Id,
                            user.Id,
                            TimeSpan.FromMinutes(minutes));
                    }
                }
                else
                {
                    Log.Warning("Can't find role {RoleId} on server {GuildId} to apply punishment",
                        roleId.Value,
                        guild.Id);
                }

                break;
            case PunishmentAction.Warn:
                await Warn(guild, user.Id, mod, 1, reason);
                break;
            case PunishmentAction.TimeOut:
                await user.SetTimeOutAsync(TimeSpan.FromMinutes(minutes));
                break;
        }
    }

    /// <summary>
    ///     Used to prevent the bot from hitting 403's when it needs to
    ///     apply punishments with insufficient permissions
    /// </summary>
    /// <param name="guild">Guild the punishment is applied in</param>
    /// <param name="punish">Punishment to apply</param>
    /// <returns>Whether the bot has sufficient permissions</returns>
    private async Task<bool> CheckPermission(IGuild guild, PunishmentAction punish)
    {
        var botUser = await guild.GetCurrentUserAsync();
        switch (punish)
        {
            case PunishmentAction.Mute:
                return botUser.GuildPermissions.MuteMembers && botUser.GuildPermissions.ManageRoles;
            case PunishmentAction.Kick:
                return botUser.GuildPermissions.KickMembers;
            case PunishmentAction.Ban:
                return botUser.GuildPermissions.BanMembers;
            case PunishmentAction.Softban:
                return botUser.GuildPermissions.BanMembers; // ban + unban
            case PunishmentAction.RemoveRoles:
                return botUser.GuildPermissions.ManageRoles;
            case PunishmentAction.ChatMute:
                return botUser.GuildPermissions.ManageRoles; // adds nadeko-mute role
            case PunishmentAction.VoiceMute:
                return botUser.GuildPermissions.MuteMembers;
            case PunishmentAction.AddRole:
                return botUser.GuildPermissions.ManageRoles;
            case PunishmentAction.TimeOut:
                return botUser.GuildPermissions.ModerateMembers;
            default:
                return true;
        }
    }

    public async Task CheckAllWarnExpiresAsync()
    {
        await using var uow = _db.GetDbContext();

        var toClear = await uow.GetTable<Warning>()
                               .Where(x => uow.GetTable<GuildConfig>()
                                              .Count(y => y.GuildId == x.GuildId
                                                          && y.WarnExpireHours > 0
                                                          && y.WarnExpireAction == WarnExpireAction.Clear)
                                           > 0
                                           && x.Forgiven == false
                                           && x.DateAdded
                                           < DateTime.UtcNow.AddHours(-uow.GetTable<GuildConfig>()
                                                                          .Where(y => x.GuildId == y.GuildId)
                                                                          .Select(y => y.WarnExpireHours)
                                                                          .First()))
                               .Select(x => x.Id)
                               .ToListAsyncLinqToDB();

        var cleared = await uow.GetTable<Warning>()
                               .Where(x => toClear.Contains(x.Id))
                               .UpdateAsync(_ => new()
                               {
                                   Forgiven = true,
                                   ForgivenBy = "expiry"
                               });

        var toDelete = await uow.GetTable<Warning>()
                                .Where(x => uow.GetTable<GuildConfig>()
                                               .Count(y => y.GuildId == x.GuildId
                                                           && y.WarnExpireHours > 0
                                                           && y.WarnExpireAction == WarnExpireAction.Delete)
                                            > 0
                                            && x.DateAdded
                                            < DateTime.UtcNow.AddHours(-uow.GetTable<GuildConfig>()
                                                                           .Where(y => x.GuildId == y.GuildId)
                                                                           .Select(y => y.WarnExpireHours)
                                                                           .First()))
                                .Select(x => x.Id)
                                .ToListAsyncLinqToDB();

        var deleted = await uow.GetTable<Warning>()
                               .Where(x => toDelete.Contains(x.Id))
                               .DeleteAsync();

        if (cleared > 0 || deleted > 0)
        {
            Log.Information("Cleared {ClearedWarnings} warnings and deleted {DeletedWarnings} warnings due to expiry",
                cleared,
                toDelete.Count);
        }
    }

    public async Task CheckWarnExpiresAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var config = uow.GuildConfigsForId(guildId, inc => inc);

        if (config.WarnExpireHours == 0)
            return;

        if (config.WarnExpireAction == WarnExpireAction.Clear)
        {
            await uow.Set<Warning>()
                     .Where(x => x.GuildId == guildId
                                 && x.Forgiven == false
                                 && x.DateAdded < DateTime.UtcNow.AddHours(-config.WarnExpireHours))
                     .UpdateAsync(_ => new()
                     {
                         Forgiven = true,
                         ForgivenBy = "expiry"
                     });
        }
        else if (config.WarnExpireAction == WarnExpireAction.Delete)
        {
            await uow.Set<Warning>()
                     .Where(x => x.GuildId == guildId
                                 && x.DateAdded < DateTime.UtcNow.AddHours(-config.WarnExpireHours))
                     .DeleteAsync();
        }

        await uow.SaveChangesAsync();
    }

    public Task<(int, bool)> GetWarnExpire(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var config = uow.GuildConfigsForId(guildId, set => set);
        return Task.FromResult((config.WarnExpireHours / 24, config.WarnExpireAction == WarnExpireAction.Delete));
    }

    public async Task WarnExpireAsync(ulong guildId, int days, bool delete)
    {
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.GuildConfigsForId(guildId, inc => inc);

            config.WarnExpireHours = days * 24;
            config.WarnExpireAction = delete ? WarnExpireAction.Delete : WarnExpireAction.Clear;
            await uow.SaveChangesAsync();

            // no need to check for warn expires
            if (config.WarnExpireHours == 0)
                return;
        }

        await CheckWarnExpiresAsync(guildId);
    }

    public IGrouping<ulong, Warning>[] WarnlogAll(ulong gid)
    {
        using var uow = _db.GetDbContext();
        return uow.Set<Warning>().GetForGuild(gid).GroupBy(x => x.UserId).ToArray();
    }

    public Warning[] UserWarnings(ulong gid, ulong userId)
    {
        using var uow = _db.GetDbContext();
        return uow.Set<Warning>().ForId(gid, userId);
    }

    public async Task<bool> WarnClearAsync(
        ulong guildId,
        ulong userId,
        int index,
        string moderator)
    {
        var toReturn = true;
        await using var uow = _db.GetDbContext();
        if (index == 0)
            await uow.Set<Warning>().ForgiveAll(guildId, userId, moderator);
        else
            toReturn = uow.Set<Warning>().Forgive(guildId, userId, moderator, index - 1);
        await uow.SaveChangesAsync();
        return toReturn;
    }

    public async Task<bool> WarnPunish(
        ulong guildId,
        int number,
        PunishmentAction punish,
        TimeSpan? time,
        IRole role = null)
    {
        // these 3 don't make sense with time
        if (punish is PunishmentAction.Softban or PunishmentAction.Kick or PunishmentAction.RemoveRoles
            && time is not null)
            return false;

        if (number <= 0 || (time is not null && time > TimeSpan.FromDays(59)))
            return false;

        if (punish is PunishmentAction.AddRole && role is null)
            return false;

        if (punish is PunishmentAction.TimeOut && time is null)
            return false;

        var timeMinutes = (int?)time?.TotalMinutes ?? 0;
        var roleId = punish == PunishmentAction.AddRole ? role!.Id : default(ulong?);
        await using var uow = _db.GetDbContext();
        await uow.GetTable<WarningPunishment>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         Count = number,
                         Punishment = punish,
                         Time = timeMinutes,
                         RoleId = roleId
                     },
                     _ => new()
                     {
                         Punishment = punish,
                         Time = timeMinutes,
                         RoleId = roleId
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         Count = number
                     });
        return true;
    }

    public async Task<bool> WarnPunishRemove(ulong guildId, int count)
    {
        await using var uow = _db.GetDbContext();
        var numDeleted = await uow.GetTable<WarningPunishment>()
                                  .DeleteAsync(x => x.GuildId == guildId && x.Count == count);

        return numDeleted > 0;
    }


    public async Task<WarningPunishment[]> WarnPunishList(ulong guildId)
    {
        await using var uow = _db.GetDbContext();

        var wps = uow.GetTable<WarningPunishment>()
                     .Where(x => x.GuildId == guildId)
                     .OrderBy(x => x.Count)
                     .ToArray();
        return wps;
    }

    public (IReadOnlyCollection<(string Original, ulong? Id, string Reason)> Bans, int Missing) MassKill(
        SocketGuild guild,
        string people)
    {
        var gusers = guild.Users;
        //get user objects and reasons
        var bans = people.Split("\n")
                         .Select(x =>
                         {
                             var split = x.Trim().Split(" ");

                             var reason = string.Join(" ", split.Skip(1));

                             if (ulong.TryParse(split[0], out var id))
                                 return (Original: split[0], Id: id, Reason: reason);

                             return (Original: split[0],
                                 gusers.FirstOrDefault(u => u.ToString().ToLowerInvariant() == x)?.Id,
                                 Reason: reason);
                         })
                         .ToArray();

        //if user is null, means that person couldn't be found
        var missing = bans.Count(x => !x.Id.HasValue);

        //get only data for found users
        var found = bans.Where(x => x.Id.HasValue).Select(x => x.Id.Value).ToList();

        _ = _blacklistService.BlacklistUsers(found);

        return (bans, missing);
    }

    public string GetBanTemplate(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var template = uow.Set<BanTemplate>().AsQueryable().FirstOrDefault(x => x.GuildId == guildId);
        return template?.Text;
    }

    public void SetBanTemplate(ulong guildId, string text)
    {
        using var uow = _db.GetDbContext();
        var template = uow.Set<BanTemplate>().AsQueryable().FirstOrDefault(x => x.GuildId == guildId);

        if (text is null)
        {
            if (template is null)
                return;

            uow.Remove(template);
        }
        else if (template is null)
        {
            uow.Set<BanTemplate>()
               .Add(new()
               {
                   GuildId = guildId,
                   Text = text
               });
        }
        else
            template.Text = text;

        uow.SaveChanges();
    }

    public async Task SetBanPruneAsync(ulong guildId, int? pruneDays)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.Set<BanTemplate>()
                 .ToLinqToDBTable()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         Text = null,
                         DateAdded = DateTime.UtcNow,
                         PruneDays = pruneDays
                     },
                     old => new()
                     {
                         PruneDays = pruneDays
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });
    }

    public async Task<int?> GetBanPruneAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.Set<BanTemplate>()
                        .Where(x => x.GuildId == guildId)
                        .Select(x => x.PruneDays)
                        .FirstOrDefaultAsyncLinqToDB();
    }

    public Task<SmartText> GetBanUserDmEmbed(
        ICommandContext context,
        IGuildUser target,
        string defaultMessage,
        string banReason,
        TimeSpan? duration)
        => GetBanUserDmEmbed((DiscordSocketClient)context.Client,
            (SocketGuild)context.Guild,
            (IGuildUser)context.User,
            target,
            defaultMessage,
            banReason,
            duration);

    public async Task<SmartText> GetBanUserDmEmbed(
        DiscordSocketClient client,
        SocketGuild guild,
        IGuildUser moderator,
        IGuildUser target,
        string defaultMessage,
        string banReason,
        TimeSpan? duration)
    {
        var template = GetBanTemplate(guild.Id);

        banReason = string.IsNullOrWhiteSpace(banReason) ? "-" : banReason;

        var repCtx = new ReplacementContext(client, guild)
                     .WithOverride("%ban.mod%", () => moderator.ToString())
                     .WithOverride("%ban.mod.fullname%", () => moderator.ToString())
                     .WithOverride("%ban.mod.name%", () => moderator.Username)
                     .WithOverride("%ban.mod.discrim%", () => moderator.Discriminator)
                     .WithOverride("%ban.user%", () => target.ToString())
                     .WithOverride("%ban.user.fullname%", () => target.ToString())
                     .WithOverride("%ban.user.name%", () => target.Username)
                     .WithOverride("%ban.user.discrim%", () => target.Discriminator)
                     .WithOverride("%reason%", () => banReason)
                     .WithOverride("%ban.reason%", () => banReason)
                     .WithOverride("%ban.duration%",
                         () => duration?.ToString(@"d\.hh\:mm") ?? "perma");


        // if template isn't set, use the old message style
        if (string.IsNullOrWhiteSpace(template))
        {
            template = JsonConvert.SerializeObject(new
            {
                color = _bcs.Data.Color.Error.PackedValue >> 8,
                description = defaultMessage
            });
        }
        // if template is set to "-" do not dm the user
        else if (template == "-")
            return default;
        // if template is an embed, send that embed with replacements
        // otherwise, treat template as a regular string with replacements
        else if (SmartText.CreateFrom(template) is not { IsEmbed: true } or { IsEmbedArray: true })
        {
            template = JsonConvert.SerializeObject(new
            {
                color = _bcs.Data.Color.Error.PackedValue >> 8,
                description = template
            });
        }

        var output = SmartText.CreateFrom(template);
        return await _repSvc.ReplaceAsync(output, repCtx);
    }

    public async Task<Warning> WarnDelete(ulong guildId, ulong userId, int index)
    {
        await using var uow = _db.GetDbContext();

        var warn = await uow.GetTable<Warning>()
                            .Where(x => x.GuildId == guildId && x.UserId == userId)
                            .OrderByDescending(x => x.DateAdded)
                            .Skip(index)
                            .FirstOrDefaultAsyncLinqToDB();

        if (warn is not null)
        {
            await uow.GetTable<Warning>()
                     .Where(x => x.Id == warn.Id)
                     .DeleteAsync();
        }

        return warn;
    }

    public async Task<bool> WarnDelete(ulong guildId, int id)
    {
        await using var uow = _db.GetDbContext();

        var numDeleted = await uow.GetTable<Warning>()
                                  .Where(x => x.GuildId == guildId && x.Id == id)
                                  .DeleteAsync();

        return numDeleted > 0;
    }

    public async Task<(IReadOnlyCollection<Warning> latest, int totalCount)> GetLatestWarnings(
        ulong guildId,
        int page = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);

        await using var uow = _db.GetDbContext();
        var latest = await uow.GetTable<Warning>()
                              .Where(x => x.GuildId == guildId)
                              .OrderByDescending(x => x.DateAdded)
                              .Skip(10 * (page - 1))
                              .Take(10)
                              .ToListAsyncLinqToDB();

        var totalCount = await uow.GetTable<Warning>()
                                  .Where(x => x.GuildId == guildId)
                                  .CountAsyncLinqToDB();

        return (latest, totalCount);
    }

    public async Task<bool> ForgiveWarning(ulong requestGuildId, int warnId, string modName)
    {
        await using var uow = _db.GetDbContext();
        var success = await uow.GetTable<Warning>()
                               .Where(x => x.GuildId == requestGuildId && x.Id == warnId)
                               .UpdateAsync(_ => new()
                               {
                                   Forgiven = true,
                                   ForgivenBy = modName,
                               })
                      == 1;

        return success;
    }

    public async Task<(IReadOnlyCollection<Warning> latest, int totalCount)> GetUserWarnings(
        ulong guildId,
        ulong userId,
        int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);

        await using var uow = _db.GetDbContext();
        var latest = await uow.GetTable<Warning>()
                              .Where(x => x.GuildId == guildId && x.UserId == userId)
                              .OrderByDescending(x => x.DateAdded)
                              .Skip(10 * (page - 1))
                              .Take(10)
                              .ToListAsyncLinqToDB();

        var totalCount = await uow.GetTable<Warning>()
                                  .Where(x => x.GuildId == guildId && x.UserId == userId)
                                  .CountAsyncLinqToDB();

        return (latest, totalCount);
    }
}