#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration._common.results;

namespace NadekoBot.Modules.Administration;

public class AdministrationService : INService, IReadyExecutor
{
    private ConcurrentHashSet<ulong> deleteMessagesOnCommand;
    private ConcurrentDictionary<ulong, bool> delMsgOnCmdChannels;

    private readonly DbService _db;
    private readonly IReplacementService _repSvc;
    private readonly ILogCommandService _logService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ShardData _shardData;
    private readonly CommandHandler _cmdHandler;

    public AdministrationService(
        DbService db,
        IReplacementService repSvc,
        ILogCommandService logService,
        IHttpClientFactory factory,
        ShardData shardData,
        CommandHandler cmdHandler)
    {
        _db = db;
        _shardData = shardData;
        _repSvc = repSvc;
        _logService = logService;
        _httpFactory = factory;
        _cmdHandler = cmdHandler;
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        deleteMessagesOnCommand = new(await uow.GetTable<GuildConfig>()
                                         .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId) && x.DeleteMessageOnCommand)
                                         .Select(x => x.GuildId)
                                         .ToListAsyncLinqToDB());

        delMsgOnCmdChannels = (await uow.GetTable<DelMsgOnCmdChannel>()
                                                   .Where(x => deleteMessagesOnCommand.Contains(x.GuildId))
                                                   .ToDictionaryAsyncLinqToDB(x => x.ChannelId, x => x.State))
                                                   .ToConcurrent();

        _cmdHandler.CommandExecuted += DelMsgOnCmd_Handler;
    }

    public async Task<(bool DelMsgOnCmd, IEnumerable<DelMsgOnCmdChannel> channels)> GetDelMsgOnCmdData(ulong guildId)
    {
        using var uow = _db.GetDbContext();

        var conf = await uow.GetTable<GuildConfig>()
        .Where(x => x.GuildId == guildId)
        .Select(x => x.DeleteMessageOnCommand)
        .FirstOrDefaultAsyncLinqToDB();

        var channels = await uow.GetTable<DelMsgOnCmdChannel>()
        .Where(x => x.GuildId == guildId)
        .ToListAsyncLinqToDB();

        return (conf, channels);
    }

    private Task DelMsgOnCmd_Handler(IUserMessage msg, CommandInfo cmd)
    {
        if (msg.Channel is not ITextChannel channel)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            //wat ?!
            if (delMsgOnCmdChannels.TryGetValue(channel.Id, out var state))
            {
                if (state && cmd.Name != "prune" && cmd.Name != "pick")
                {
                    _logService.AddDeleteIgnore(msg.Id);
                    try
                    { await msg.DeleteAsync(); }
                    catch { }
                }
                //if state is false, that means do not do it
            }
            else if (deleteMessagesOnCommand.Contains(channel.Guild.Id) && cmd.Name != "prune" && cmd.Name != "pick")
            {
                _logService.AddDeleteIgnore(msg.Id);
                try
                { await msg.DeleteAsync(); }
                catch { }
            }
        });
        return Task.CompletedTask;
    }

    public async Task<bool> ToggleDeleteMessageOnCommand(ulong guildId)
    {
        using var uow = _db.GetDbContext();

        var conf = await uow.GetTable<GuildConfig>()
            .Where(x => x.GuildId == guildId)
            .UpdateWithOutputAsync(x => new()
            {
                DeleteMessageOnCommand = !x.DeleteMessageOnCommand
            });

        if (conf.Length == 0)
            return false;

        var val = conf[0].Inserted.DeleteMessageOnCommand;

        if (val)
            deleteMessagesOnCommand.Add(guildId);
        else
            deleteMessagesOnCommand.TryRemove(guildId);

        return val;
    }

    public async Task SetDelMsgOnCmdState(ulong guildId, ulong chId, Administration.State newState)
    {
        await using (var uow = _db.GetDbContext())
        {
            var old = await uow.GetTable<DelMsgOnCmdChannel>()
                .Where(x => x.GuildId == guildId && x.ChannelId == chId)
                .FirstOrDefaultAsyncLinqToDB();

            if (newState == Administration.State.Inherit)
            {
                if (old is not null)
                {
                    uow.Remove(old);
                }
            }
            else
            {
                if (old is null)
                {
                    old = new DelMsgOnCmdChannel
                    {
                        GuildId = guildId,
                        ChannelId = chId,
                        State = newState == Administration.State.Enable
                    };
                    uow.Add(old);
                }

                old.State = newState == Administration.State.Enable;
                delMsgOnCmdChannels[chId] = newState == Administration.State.Enable;
            }

            await uow.SaveChangesAsync();
        }

        if (newState == Administration.State.Disable)
        {
        }
        else if (newState == Administration.State.Enable)
        {
            delMsgOnCmdChannels[chId] = true;
        }
        else
        {
            delMsgOnCmdChannels.TryRemove(chId, out _);
        }
    }

    public async Task DeafenUsers(bool value, params IGuildUser[] users)
    {
        if (!users.Any())
            return;
        foreach (var u in users)
        {
            try
            {
                await u.ModifyAsync(usr => usr.Deaf = value);
            }
            catch
            {
                // ignored
            }
        }
    }

    public async Task EditMessage(
        ICommandContext context,
        ITextChannel chanl,
        ulong messageId,
        string input)
    {
        var msg = await chanl.GetMessageAsync(messageId);

        if (msg is not IUserMessage umsg || msg.Author.Id != context.Client.CurrentUser.Id)
            return;

        var repCtx = new ReplacementContext(context);

        var text = SmartText.CreateFrom(input);
        text = await _repSvc.ReplaceAsync(text, repCtx);

        await umsg.EditAsync(text);
    }

    public async Task<SetServerBannerResult> SetServerBannerAsync(IGuild guild, string img)
    {
        if (!IsValidUri(img))
            return SetServerBannerResult.InvalidURL;

        var uri = new Uri(img);

        using var http = _httpFactory.CreateClient();
        using var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        if (!sr.IsImage())
            return SetServerBannerResult.InvalidFileType;

        if (sr.GetContentLength() > 8.Megabytes())
        {
            return SetServerBannerResult.Toolarge;
        }

        await using var imageStream = await sr.Content.ReadAsStreamAsync();

        await guild.ModifyAsync(x => x.Banner = new Image(imageStream));
        return SetServerBannerResult.Success;
    }

    public async Task<SetServerIconResult> SetServerIconAsync(IGuild guild, string img)
    {
        if (!IsValidUri(img))
            return SetServerIconResult.InvalidURL;

        var uri = new Uri(img);

        using var http = _httpFactory.CreateClient();
        using var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        if (!sr.IsImage())
            return SetServerIconResult.InvalidFileType;

        await using var imageStream = await sr.Content.ReadAsStreamAsync();

        await guild.ModifyAsync(x => x.Icon = new Image(imageStream));
        return SetServerIconResult.Success;
    }

    private bool IsValidUri(string img) => !string.IsNullOrWhiteSpace(img) && Uri.IsWellFormedUriString(img, UriKind.Absolute);
}