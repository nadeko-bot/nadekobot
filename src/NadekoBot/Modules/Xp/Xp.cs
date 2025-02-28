#nullable disable warnings
using NadekoBot.Modules.Xp.Services;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Xp;

public partial class Xp : NadekoModule<XpService>
{
    public enum Channel
    {
        Channel
    }

    public enum NotifyPlace
    {
        Server = 0,
        Guild = 0,
        Global = 1
    }

    public enum Role
    {
        Role
    }

    public enum Server
    {
        Server
    }

    private readonly DownloadTracker _tracker;
    private readonly ICurrencyProvider _gss;
    private readonly XpTemplateService _templateService;

    public Xp(DownloadTracker tracker, ICurrencyProvider gss, XpTemplateService templateService)
    {
        _tracker = tracker;
        _gss = gss;
        _templateService = templateService;
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Experience([Leftover] IUser user = null)
    {
        user ??= ctx.User;
        await ctx.Channel.TriggerTypingAsync();
        var (img, fmt) = await _service.GenerateXpImageAsync((IGuildUser)user);
        await using (img)
        {
            await ctx.Channel.SendFileAsync(img, $"{ctx.Guild.Id}_{user.Id}_xp.{fmt.FileExtensions.FirstOrDefault()}");
        }
    }

    [Cmd]
    [NadekoOptions<LbOpts>]
    [Priority(0)]
    [RequireContext(ContextType.Guild)]
    public Task XpLeaderboard(params string[] args)
        => XpLeaderboard(1, args);

    [Cmd]
    [NadekoOptions<LbOpts>]
    [Priority(1)]
    [RequireContext(ContextType.Guild)]
    public async Task XpLeaderboard(int page = 1, params string[] args)
    {
        if (--page < 0 || page > 100)
            return;

        var (opts, _) = OptionsParser.ParseFrom(new LbOpts(), args);

        await ctx.Channel.TriggerTypingAsync();
        if (opts.Clean)
        {
            await _tracker.EnsureUsersDownloadedAsync(ctx.Guild);
        }

        async Task<IReadOnlyCollection<UserXpStats>> GetPageItems(int curPage)
        {
            var socketGuild = (SocketGuild)ctx.Guild;
            if (opts.Clean)
            {
                return await _service.GetGuildUserXps(ctx.Guild.Id,
                    socketGuild.Users.Select(x => x.Id).ToList(),
                    curPage);
            }

            return await _service.GetGuildUserXps(ctx.Guild.Id, curPage);
        }

        await Response()
              .Paginated()
              .PageItems(GetPageItems)
              .PageSize(10)
              .CurrentPage(page)
              .Page((users, curPage) =>
              {
                  var embed = CreateEmbed().WithTitle(GetText(strs.server_leaderboard)).WithOkColor();

                  if (!users.Any())
                      return embed.WithDescription("-");

                  for (var i = 0; i < users.Count; i++)
                  {
                      var levelStats = new LevelStats(users[i].Xp);
                      var user = ((SocketGuild)ctx.Guild).GetUser(users[i].UserId);

                      var userXpData = users[i];

                      var awardStr = string.Empty;

                      embed.AddField($"#{i + 1 + (curPage * 10)} {user?.ToString() ?? users[i].UserId.ToString()}",
                          $"{GetText(strs.level_x(levelStats.Level))} - {levelStats.TotalXp}xp {awardStr}");
                  }

                  return embed;
              })
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task XpGlobalLeaderboard(int page = 1, params string[] args)
    {
        if (--page < 0 || page > 99)
            return;

        var (opts, _) = OptionsParser.ParseFrom(new LbOpts(), args);

        await ctx.Channel.TriggerTypingAsync();
        if (opts.Clean)
        {
            await _tracker.EnsureUsersDownloadedAsync(ctx.Guild);
        }

        async Task<IReadOnlyCollection<DiscordUser>> GetPageItems(int curPage)
        {
            if (opts.Clean)
            {
                return await _service.GetGlobalUserXps(page, ((SocketGuild)ctx.Guild).Users.Select(x => x.Id).ToList());
            }

            return await _service.GetGlobalUserXps(curPage);
        }

        await Response()
              .Paginated()
              .PageItems(GetPageItems)
              .PageSize(10)
              .Page((users, curPage) =>
              {
                  var embed = CreateEmbed()
                              .WithOkColor()
                              .WithTitle(GetText(strs.global_leaderboard));

                  if (!users.Any())
                  {
                      embed.WithDescription("-");
                      return embed;
                  }

                  for (var i = 0; i < users.Count; i++)
                  {
                      var user = users[i];
                      embed.AddField($"#{i + 1 + (curPage * 10)} {user}",
                          $"{GetText(strs.level_x(new LevelStats(users[i].TotalXp).Level))} - {users[i].TotalXp}xp");
                  }

                  return embed;
              })
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(1)]
    public Task XpLevelSet(int level, IGuildUser user)
        => XpLevelSet(level, user.Id);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(0)]
    public async Task XpLevelSet(int level, ulong userId)
    {
        if (level < 0)
            return;

        await _service.SetLevelAsync(ctx.Guild.Id, userId, level);
        await Response()
              .Confirm(strs.level_set($"<@{userId}>", Format.Bold(level.ToString())))
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(2)]
    public async Task XpAdd(long amount, [Remainder] SocketRole role)
    {
        if (amount == 0)
            return;

        if (role.IsManaged)
            return;

        var count = await _service.AddXpToUsersAsync(ctx.Guild.Id,
            amount,
            role.Members.Select(x => x.Id).ToArray());
        await Response()
              .Confirm(
                  strs.xpadd_users(Format.Bold(amount.ToString()), Format.Bold(count.ToString())))
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(3)]
    public async Task XpAdd(int amount, ulong userId)
    {
        if (amount == 0)
            return;

        await _service.AddXpAsync(userId, ctx.Guild.Id, amount);
        var usr = ((SocketGuild)ctx.Guild).GetUser(userId)?.ToString() ?? userId.ToString();
        await Response().Confirm(strs.modified(Format.Bold(usr), Format.Bold(amount.ToString()))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(4)]
    public Task XpAdd(int amount, [Leftover] IGuildUser user)
        => XpAdd(amount, user.Id);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    public async Task XpTemplateReload()
    {
        _templateService.ReloadXpTemplate();
        await Task.Delay(1000);
        await Response().Confirm(strs.template_reloaded).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    public Task XpReset(IGuildUser user)
        => XpReset(user.Id);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    public async Task XpReset(ulong userId)
    {
        var embed = CreateEmbed()
                    .WithTitle(GetText(strs.reset))
                    .WithDescription(GetText(strs.reset_user_confirm));

        if (!await PromptUserConfirmAsync(embed))
            return;

        await _service.XpReset(ctx.Guild.Id, userId);

        await Response().Confirm(strs.reset_user(userId)).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    public async Task XpReset()
    {
        var embed = CreateEmbed()
                    .WithTitle(GetText(strs.reset))
                    .WithDescription(GetText(strs.reset_server_confirm));

        if (!await PromptUserConfirmAsync(embed))
            return;

        _service.XpReset(ctx.Guild.Id);

        await Response().Confirm(strs.reset_server).SendAsync();
    }

    public enum XpShopInputType
    {
        Backgrounds = 0,
        B = 0,
        Bg = 0,
        Bgs = 0,
        Frames = 1,
        F = 1,
        Fr = 1,
        Frs = 1,
        Fs = 1,
    }

    [Cmd]
    public async Task XpShop()
    {
        if (!_service.IsShopEnabled())
        {
            await Response().Error(strs.xp_shop_disabled).SendAsync();
            return;
        }

        await Response()
              .Confirm(GetText(strs.available_commands),
                  $"""
                   `{prefix}xpshop bgs`
                   `{prefix}xpshop frames`

                   *{GetText(strs.xpshop_website)}*
                   """)
              .SendAsync();
    }

    [Cmd]
    public async Task XpShop(XpShopInputType type, int page = 1)
    {
        --page;

        if (page < 0)
            return;

        var allItems = type == XpShopInputType.Backgrounds
            ? await _service.GetShopBgs()
            : await _service.GetShopFrames();

        if (allItems is null)
        {
            await Response().Error(strs.xp_shop_disabled).SendAsync();
            return;
        }

        if (allItems.Count == 0)
        {
            await Response().Error(strs.not_found).SendAsync();
            return;
        }

        await Response()
              .Paginated()
              .Items(allItems)
              .PageSize(1)
              .CurrentPage(page)
              .AddFooter(false)
              .Page((items, _) =>
              {
                  if (!items.Any())
                      return CreateEmbed()
                             .WithDescription(GetText(strs.not_found))
                             .WithErrorColor();

                  var (key, item) = items.FirstOrDefault();

                  var eb = CreateEmbed()
                           .WithOkColor()
                           .WithTitle(item.Name)
                           .AddField(GetText(strs.price),
                               CurrencyHelper.N(item.Price, Culture, _gss.GetCurrencySign()),
                               true)
                           .WithImageUrl(string.IsNullOrWhiteSpace(item.Preview)
                               ? item.Url
                               : item.Preview);

                  if (!string.IsNullOrWhiteSpace(item.Desc))
                      eb.AddField(GetText(strs.desc), item.Desc);

#if GLOBAL_NADEKO
                  if (key == "default")
                      eb.WithDescription(GetText(strs.xpshop_website));
#endif

                  var tier = _service.GetXpShopTierRequirement(type);
                  if (tier != PatronTier.None)
                  {
                      eb.WithFooter(GetText(strs.xp_shop_buy_required_tier(tier.ToString())));
                  }

                  return eb;
              })
              .Interaction(async current =>
              {
                  var (key, _) = allItems.Skip(current).First();

                  var itemType = type == XpShopInputType.Backgrounds
                      ? XpShopItemType.Background
                      : XpShopItemType.Frame;

                  var ownedItem = await _service.GetUserItemAsync(ctx.User.Id, itemType, key);
                  if (ownedItem is not null)
                  {
                      var button = new ButtonBuilder(ownedItem.IsUsing
                              ? GetText(strs.in_use)
                              : GetText(strs.use),
                          "xpshop:use",
                          emote: Emoji.Parse("👐"),
                          isDisabled: ownedItem.IsUsing);

                      var inter = _inter.Create(
                          ctx.User.Id,
                          button,
                          OnShopUse,
                          (key, itemType),
                          clearAfter: false);

                      return inter;
                  }
                  else
                  {
                      var button = new ButtonBuilder(GetText(strs.buy),
                          "xpshop:buy",
                          emote: Emoji.Parse("💰"));

                      var inter = _inter.Create(
                          ctx.User.Id,
                          button,
                          OnShopBuy,
                          (key, itemType),
                          singleUse: true,
                          clearAfter: false);

                      return inter;
                  }
              })
              .SendAsync();
    }

    [Cmd]
    public async Task XpShopBuy(XpShopInputType type, string key)
    {
        var result = await _service.BuyShopItemAsync(ctx.User.Id, (XpShopItemType)type, key);

        NadekoInteractionBase GetUseInteraction()
        {
            return _inter.Create(ctx.User.Id,
                new(label: "Use", customId: "xpshop:use_item", emote: Emoji.Parse("👐")),
                async (_, state) => await XpShopUse(state.type, state.key),
                (type, key)
            );
        }

        if (result != BuyResult.Success)
        {
            var _ = result switch
            {
                BuyResult.XpShopDisabled => await Response().Error(strs.xp_shop_disabled).SendAsync(),
                BuyResult.InsufficientFunds => await Response()
                                                     .Error(strs.not_enough(_gss.GetCurrencySign()))
                                                     .SendAsync(),
                BuyResult.AlreadyOwned =>
                    await Response().Error(strs.xpshop_already_owned).Interaction(GetUseInteraction()).SendAsync(),
                BuyResult.UnknownItem => await Response().Error(strs.xpshop_item_not_found).SendAsync(),
                BuyResult.InsufficientPatronTier => await Response().Error(strs.patron_insuff_tier).SendAsync(),
                _ => throw new ArgumentOutOfRangeException()
            };
            return;
        }

        await Response()
              .Confirm(strs.xpshop_buy_success(type.ToString().ToLowerInvariant(),
                  key.ToLowerInvariant()))
              .Interaction(GetUseInteraction())
              .SendAsync();
    }

    [Cmd]
    public async Task XpShopUse(XpShopInputType type, string key)
    {
        var result = await _service.UseShopItemAsync(ctx.User.Id, (XpShopItemType)type, key);

        if (!result)
        {
            await Response().Confirm(strs.xp_shop_item_cant_use).SendAsync();
            return;
        }

        await ctx.OkAsync();
    }

    private async Task OnShopUse(SocketMessageComponent smc, (string key, XpShopItemType type) state)
    {
        var (key, type) = state;

        var result = await _service.UseShopItemAsync(ctx.User.Id, type, key);


        if (!result)
        {
            await Response().Confirm(strs.xp_shop_item_cant_use).SendAsync();
        }
    }

    private async Task OnShopBuy(SocketMessageComponent smc, (string key, XpShopItemType type) state)
    {
        var (key, type) = state;

        var result = await _service.BuyShopItemAsync(ctx.User.Id, type, key);

        if (result == BuyResult.InsufficientFunds)
        {
            await Response().Error(strs.not_enough(_gss.GetCurrencySign())).SendAsync();
        }
        else if (result == BuyResult.Success)
        {
            await _service.UseShopItemAsync(ctx.User.Id, type, key);
        }
    }
}