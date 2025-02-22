#nullable disable
using System.Globalization;

// ReSharper disable InconsistentNaming

namespace NadekoBot.Common;

[UsedImplicitly(ImplicitUseTargetFlags.Default
                | ImplicitUseTargetFlags.WithInheritors
                | ImplicitUseTargetFlags.WithMembers)]
public abstract class NadekoModule : ModuleBase
{
    protected CultureInfo Culture { get; set; }

    // Injected by Discord.net
    public IBotStrings Strings { get; set; }
    public ICommandHandler _cmdHandler { get; set; }
    public ILocalization _localization { get; set; }
    public INadekoInteractionService _inter { get; set; }
    public IReplacementService repSvc { get; set; }
    public IMessageSenderService _sender { get; set; }

    protected string prefix
        => _cmdHandler.GetPrefix(ctx.Guild);

    protected ICommandContext ctx
        => Context;

    protected EmbedBuilder CreateEmbed()
        => _sender.CreateEmbed(ctx.Guild?.Id);
    
    public ResponseBuilder Response()
        => new ResponseBuilder(Strings, _sender, (DiscordSocketClient)ctx.Client)
            .Context(ctx);

    protected override void BeforeExecute(CommandInfo command)
        => Culture = _localization.GetCultureInfo(ctx.Guild?.Id);

    protected string GetText(in LocStr data)
        => Strings.GetText(data, Culture);

    // localized normal
    public async Task<bool> PromptUserConfirmAsync(EmbedBuilder embed)
    {
        embed.WithPendingColor()
             .WithFooter("yes/no");

        var msg = await Response().Embed(embed).SendAsync();
        try
        {
            var input = await GetUserInputAsync(ctx.User.Id, ctx.Channel.Id);
            input = input?.ToUpperInvariant();

            if (input != "YES" && input != "Y")
                return false;

            return true;
        }
        finally
        {
            _ = Task.Run(() => msg.DeleteAsync());
        }
    }

    // TypeConverter typeConverter = TypeDescriptor.GetConverter(propType); ?
    public async Task<string> GetUserInputAsync(ulong userId, ulong channelId, Func<string, bool> validate = null)
    {
        var userInputTask = new TaskCompletionSource<string>();
        var dsc = (DiscordSocketClient)ctx.Client;
        try
        {
            dsc.MessageReceived += MessageReceived;

            if (await Task.WhenAny(userInputTask.Task, Task.Delay(10000)) != userInputTask.Task)
                return null;

            return await userInputTask.Task;
        }
        finally
        {
            dsc.MessageReceived -= MessageReceived;
        }

        Task MessageReceived(SocketMessage arg)
        {
            _ = Task.Run(() =>
            {
                if (arg is not SocketUserMessage userMsg
                    || userMsg.Channel is not ITextChannel
                    || userMsg.Author.Id != userId
                    || userMsg.Channel.Id != channelId)
                    return Task.CompletedTask;

                if (validate is not null && !validate(arg.Content))
                    return Task.CompletedTask;

                if (userInputTask.TrySetResult(arg.Content))
                    userMsg.DeleteAfter(1);

                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        }
    }

    protected async Task<bool> CheckRoleHierarchy(IGuildUser target)
    {
        var curUser = ((SocketGuild)ctx.Guild).CurrentUser;
        var ownerId = ctx.Guild.OwnerId;
        var modMaxRole = ((IGuildUser)ctx.User).GetRoles().Max(r => r.Position);
        var targetMaxRole = target.GetRoles().Max(r => r.Position);
        var botMaxRole = curUser.GetRoles().Max(r => r.Position);
        // bot can't punish a user who is higher in the hierarchy. Discord will return 403
        // moderator can be owner, in which case role hierarchy doesn't matter
        // otherwise, moderator has to have a higher role
        if (botMaxRole <= targetMaxRole
            || (ctx.User.Id != ownerId && targetMaxRole >= modMaxRole)
            || target.Id == ownerId)
        {
            await Response().Error(strs.hierarchy).SendAsync();
            return false;
        }

        return true;
    }

    protected async Task<bool> CheckRoleHierarchy(IRole role)
    {
        var botUser = ((SocketGuild)ctx.Guild).CurrentUser;
        var ownerId = ctx.Guild.OwnerId;
        var modMaxRole = ((IGuildUser)ctx.User).GetRoles().Max(r => r.Position);
        var botMaxRole = botUser.GetRoles().Max(r => r.Position);

        // role must be lower than the bot role
        // and the mod must have a higher role
        if (botMaxRole <= role.Position
            || (ctx.User.Id != ownerId && role.Position >= modMaxRole))
        {
            await Response().Error(strs.hierarchy).SendAsync();
            return false;
        }

        return true;
    }
}

public abstract class NadekoModule<TService> : NadekoModule
{
    public TService _service { get; set; }
}