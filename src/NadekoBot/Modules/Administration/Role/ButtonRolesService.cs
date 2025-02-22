using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using LinqToDB.SqlQuery;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NCalc;

namespace NadekoBot.Modules.Administration.Services;

public sealed class ButtonRolesService : INService, IReadyExecutor
{
    private const string BTN_PREFIX = "n:btnrole:";

    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;

    public ButtonRolesService(IBotCreds creds, DiscordSocketClient client, DbService db)
    {
        _creds = creds;
        _client = client;
        _db = db;
    }


    public Task OnReadyAsync()
    {
        _client.InteractionCreated += OnInteraction;

        return Task.CompletedTask;
    }

    private async Task OnInteraction(SocketInteraction inter)
    {
        if (inter is not SocketMessageComponent smc)
            return;

        if (!smc.Data.CustomId.StartsWith(BTN_PREFIX))
            return;

        await inter.DeferAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await using var uow = _db.GetDbContext();
                var buttonRole = await uow.GetTable<ButtonRole>()
                                          .Where(x => x.ButtonId == smc.Data.CustomId && x.MessageId == smc.Message.Id)
                                          .FirstOrDefaultAsyncLinqToDB();

                if (buttonRole is null)
                    return;

                var guild = _client.GetGuild(buttonRole.GuildId);
                if (guild is null)
                    return;

                var role = guild.GetRole(buttonRole.RoleId);
                if (role is null)
                    return;

                if (smc.User is not IGuildUser user)
                    return;

                if (user.GetRoles().Any(x => x.Id == role.Id))
                {
                    await user.RemoveRoleAsync(role.Id);
                    return;
                }

                if (buttonRole.Exclusive)
                {
                    var otherRoles = await uow.GetTable<ButtonRole>()
                                              .Where(x => x.GuildId == smc.GuildId && x.MessageId == smc.Message.Id)
                                              .Select(x => x.RoleId)
                                              .ToListAsyncLinqToDB();

                    await user.RemoveRolesAsync(otherRoles);
                }

                await user.AddRoleAsync(role.Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to handle button role interaction for user {UserId}", inter.User.Id);
            }
        });
    }

    public async Task<bool> AddButtonRole(
        ulong guildId,
        ulong channelId,
        ulong roleId,
        ulong messageId,
        IEmote emote
    )
    {
        await using var uow = _db.GetDbContext();

        // up to 25 per message
        if (await uow.GetTable<ButtonRole>()
                     .Where(x => x.MessageId == messageId)
                     .CountAsyncLinqToDB()
            >= 25)
            return false;


        var emoteStr = emote.ToString()!;
        var guid = Guid.NewGuid();
        await uow.GetTable<ButtonRole>()
                 .InsertOrUpdateAsync(() => new ButtonRole()
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         RoleId = roleId,
                         MessageId = messageId,
                         Position =
                             uow
                                 .GetTable<ButtonRole>()
                                 .Any(x => x.MessageId == messageId)
                                 ? uow.GetTable<ButtonRole>()
                                      .Where(x => x.MessageId == messageId)
                                      .Max(x => x.Position)
                                 : 1,
                         Emote = emoteStr,
                         Label = string.Empty,
                         ButtonId = $"{BTN_PREFIX}:{guildId}:{guid}",
                         Exclusive = uow.GetTable<ButtonRole>()
                                        .Any(x => x.GuildId == guildId && x.MessageId == messageId)
                                     && uow.GetTable<ButtonRole>()
                                           .Where(x => x.GuildId == guildId && x.MessageId == messageId)
                                           .All(x => x.Exclusive)
                     },
                     _ => new()
                     {
                         Emote = emoteStr,
                         Label = string.Empty,
                         ButtonId = $"{BTN_PREFIX}:{guildId}:{guid}"
                     },
                     () => new()
                     {
                         RoleId = roleId,
                         MessageId = messageId,
                     });

        return true;
    }

    public async Task<IReadOnlyList<ButtonRole>> RemoveButtonRoles(ulong guildId, ulong messageId)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<ButtonRole>()
                        .Where(x => x.GuildId == guildId && x.MessageId == messageId)
                        .DeleteWithOutputAsync();
    }

    public async Task<ButtonRole?> RemoveButtonRole(ulong guildId, ulong messageId, ulong roleId)
    {
        await using var uow = _db.GetDbContext();
        var deleted = await uow.GetTable<ButtonRole>()
                               .Where(x => x.GuildId == guildId && x.MessageId == messageId && x.RoleId == roleId)
                               .DeleteWithOutputAsync();

        return deleted.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ButtonRole>> GetButtonRoles(ulong guildId, ulong? messageId)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<ButtonRole>()
                        .Where(x => x.GuildId == guildId && (messageId == null || x.MessageId == messageId))
                        .OrderBy(x => x.Id)
                        .ToListAsyncLinqToDB();
    }

    public async Task<bool> SetExclusiveButtonRoles(ulong guildId, ulong messageId, bool exclusive)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<ButtonRole>()
                        .Where(x => x.GuildId == guildId && x.MessageId == messageId)
                        .UpdateAsync((_) => new()
                        {
                            Exclusive = exclusive
                        }) > 0;
    }
}