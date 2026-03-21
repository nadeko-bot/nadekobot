using System.Text;
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.Waifus.Waifu;
using NadekoBot.Services;

namespace NadekoBot.Modules.Waifus;

public partial class Waifus
{
    public class WaifuCommands(WaifuService svc, ICurrencyProvider cp, IBankService bank, ImagesConfig ic) : NadekoModule
    {
    private static readonly NadekoRandom _rng = new();
    private static string BuildProgressBar(double fraction, int length, char filled = '█', char empty = '░')
    {
        var filledCount = (int)Math.Round(fraction * length);
        filledCount = Math.Clamp(filledCount, 0, length);
        return new string(filled, filledCount) + new string(empty, length - filledCount);
    }

    private static Discord.Color GetConditionColor(int conditionPercent)
        => conditionPercent switch
        {
            > 60 => new Discord.Color(0x2E, 0xCC, 0x71), // green
            > 30 => new Discord.Color(0xF3, 0x9C, 0x12), // amber
            _ => new Discord.Color(0xE7, 0x4C, 0x3C)      // red
        };

    private NadekoInteractionBase CreateWaifuCardInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                customId: "woptin:show_card",
                emote: new Emoji("👤")),
            async smc =>
            {
                await ShowWaifuCardAsync(ctx.User.Id);
                await smc.DeferAsync();
            });

    private NadekoInteractionBase CreateBankInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                customId: "wback:bank_balance",
                emote: new Emoji("🏦")),
            BankAction);

    private NadekoInteractionBase CreateOptInInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                label: GetText(strs.waifu_btn_opt_in),
                customId: "waifu:opt_in",
                emote: new Emoji("🎀"),
                style: ButtonStyle.Success),
            async smc =>
            {
                var res = await svc.OptInAsync(ctx.User.Id);
                await res.Match(
                    _ => smc.RespondConfirmAsync(_sender, GetText(strs.waifu_optin_already)),
                    _ => smc.RespondAsync(_sender, GetText(strs.waifu_optin_no_funds), MsgType.Error),
                    _ => smc.RespondConfirmAsync(_sender, GetText(strs.waifu_optin_success(prefix))));
            });

    private NadekoInteractionBase CreateWaifuListInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                label: GetText(strs.waifu_btn_my_waifus),
                customId: "waifu:my_waifus",
                emote: new Emoji("📋")),
            async smc =>
            {
                await WaifuListInternalAsync(ctx.User.Id);
                await smc.DeferAsync();
            });

    private NadekoInteractionBase CreateCollectPayoutInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                label: GetText(strs.waifu_btn_collect),
                customId: "waifu:collect_payout",
                emote: new Emoji("💰"),
                style: ButtonStyle.Success),
            async smc =>
            {
                await WaifuPayout();
                await smc.DeferAsync();
            });

    private NadekoInteractionBase CreateCollectPayoutConfirmInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                label: GetText(strs.waifu_btn_collect),
                customId: "waifu:collect_confirm",
                emote: new Emoji("💰"),
                style: ButtonStyle.Success),
            async smc =>
            {
                var result = await svc.ClaimPayoutAsync(ctx.User.Id);
                var currSign = cp.GetCurrencySign();
                await result.Match(
                    _ => smc.RespondAsync(_sender, GetText(strs.waifu_no_pending_payout), MsgType.Error, ephemeral: true),
                    claimed => smc.RespondConfirmAsync(_sender,
                        GetText(strs.waifu_payout_claimed(CurrencyHelper.N(claimed.Value, Culture, currSign))),
                        ephemeral: true));
            });

    private async Task BankAction(SocketMessageComponent smc)
    {
        var balance = await bank.GetBalanceAsync(ctx.User.Id);
        var currSign = cp.GetCurrencySign();
        await smc.RespondConfirmAsync(_sender,
            GetText(strs.waifu_bank_balance(CurrencyHelper.N(balance, Culture, currSign))),
            ephemeral: true);
    }

    private async Task ShowWaifuCardAsync(ulong targetUserId, string? banner = null)
    {
        var info = await svc.GetWaifuInfoAsync(targetUserId);
        if (info is null)
        {
            await Response().Error(strs.waifu_not_opted_in).SendAsync();
            return;
        }

        var targetUser = await ctx.Client.GetUserAsync(targetUserId);

        var manager = info.ManagerId.HasValue
            ? await ctx.Client.GetUserAsync(info.ManagerId.Value)
            : null;

        var moodPercent = info.Mood / 10;
        var foodPercent = info.Food / 10;
        var condition = (moodPercent + foodPercent) / 2;
        var cycleProgress = svc.GetCycleProgressFraction();
        var cyclePercent = (int)Math.Round(cycleProgress * 100);
        var payoutUnix = new DateTimeOffset(svc.GetNextCycleTime()).ToUnixTimeSeconds();
        var cycleNum = svc.GetCurrentCycle();
        var currSign = cp.GetCurrencySign();

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(banner))
            sb.AppendLine($"> {banner}");

        if (!string.IsNullOrWhiteSpace(info.Quote))
            sb.AppendLine($"*\"{info.Quote}\"*");

        if (!string.IsNullOrWhiteSpace(info.Description))
            sb.AppendLine(info.Description);

        if (sb.Length > 0)
            sb.AppendLine();

        sb.AppendLine(GetText(strs.waifu_earned(CurrencyHelper.N(info.TotalProduced, Culture, currSign))));
        sb.AppendLine();
        sb.AppendLine($"😊 `{BuildProgressBar(moodPercent / 100.0, 10, '▰', '▱')}` {moodPercent}%");
        sb.AppendLine($"🍔 `{BuildProgressBar(foodPercent / 100.0, 10, '▰', '▱')}` {foodPercent}%");

        var statMultiplier = (info.Mood + info.Food) / 2000.0;
        var snapshotBacked = info.SnapshotTotalBacked;
        var overCap = snapshotBacked > info.ReturnsCap && snapshotBacked > 0;
        var capMultiplier = snapshotBacked > 0
            ? Math.Min(1.0, info.ReturnsCap / (double)snapshotBacked)
            : 1.0;
        var efficiency = (int)Math.Round(statMultiplier * capMultiplier * 100);
        var effBar = BuildProgressBar(efficiency / 100.0, 10, '▰', '▱');
        var efficiencyText = overCap
            ? $"⚡ `{effBar}` {efficiency}% ({GetText(strs.waifu_over_cap)})"
            : $"⚡ `{effBar}` {efficiency}%";
        sb.AppendLine(efficiencyText);
        sb.AppendLine();

        sb.AppendLine(GetText(strs.waifu_cycle_title(cycleNum)));
        sb.AppendLine($"`{BuildProgressBar(cycleProgress, 20)}` {cyclePercent}%");
        sb.AppendLine($"{GetText(strs.waifu_payout_time($"<t:{payoutUnix}:R>"))}");
        sb.AppendLine();

        if (info.ManagerId is null)
        {
            sb.AppendLine($"⚠\uFE0F **{GetText(strs.waifu_no_manager_title)}**");
            sb.AppendLine(GetText(strs.waifu_no_manager_payout));
        }
        else
        {
            sb.AppendLine();
        }

        var backedBySb = new StringBuilder();
        backedBySb.Append(GetText(strs.waifu_fan_count(info.FanCount)));
        if (snapshotBacked > 0)
            backedBySb.Append($"\n{CurrencyHelper.N(snapshotBacked, Culture, currSign)}");


        var displayName = targetUser?.ToString() ?? targetUserId.ToString();
        var eb = CreateEmbed()
            .WithColor(GetConditionColor(condition))
            .WithAuthor(displayName, targetUser?.GetAvatarUrl() ?? targetUser?.GetDefaultAvatarUrl())
            .WithDescription(sb.ToString())
            .AddField(GetText(strs.waifu_field_backed_by), backedBySb.ToString(), true)
            .AddField(GetText(strs.waifu_field_manager), manager?.ToString() ?? "-", true)
            .AddField(GetText(strs.waifu_field_price), CurrencyHelper.N(info.Price, Culture, currSign), true);

        var isOwnCard = targetUserId == ctx.User.Id;
        var footerParts = new List<string>
        {
            GetText(strs.waifu_footer_fee(info.WaifuFeePercent))
        };

        if (isOwnCard)
        {
            var remaining = await svc.GetRemainingActionsAsync(ctx.User.Id);
            var maxActions = await svc.GetMaxActionsAsync(ctx.User.Id);
            footerParts.Add(GetText(strs.waifu_footer_actions(remaining, maxActions)));
        }

        eb.WithFooter(string.Join(" | ", footerParts));

        if (!string.IsNullOrEmpty(info.CustomAvatarUrl))
            eb.WithThumbnailUrl(info.CustomAvatarUrl);

        var gifts = await svc.GetGiftCountsAsync(targetUserId);
        if (gifts.Count > 0)
        {
            var giftText = string.Join("  ", gifts.Select(g => $"{g.Item.Emoji}x{g.Count}"));
            eb.AddField(GetText(strs.waifu_gifts_received), giftText, false);
        }

        var resp = Response().Embed(eb);

        if (isOwnCard)
        {
            var pending = await svc.GetPendingPayoutAsync(ctx.User.Id);
            if (pending >= 1)
                resp = resp.Interaction(CreateCollectPayoutInteraction());
        }

        await resp.SendAsync();
    }

    [Cmd]
    public async Task WaifuHelp()
    {
        var eb = CreateEmbed()
            .WithOkColor()
            .WithTitle(GetText(strs.waifu_not_participating_title))
            .WithDescription(GetText(strs.waifu_not_participating(prefix)))
            .WithFooter(GetText(strs.waifu_help_footer(prefix)));

        await Response().Embed(eb).SendAsync();
    }

    [Cmd]
    public Task Waifu([Leftover] IUser? user = null)
        => WaifuInternalAsync(user?.Id);

    [Cmd]
    public Task Waifu(ulong userId)
        => WaifuInternalAsync(userId);

    private async Task WaifuInternalAsync(ulong? targetUserId)
    {
        if (targetUserId is not null)
        {
            await ShowWaifuCardAsync(targetUserId.Value);
            return;
        }

        // No argument: check if caller is a waifu
        var isWaifu = await svc.IsWaifuAsync(ctx.User.Id);
        if (isWaifu)
        {
            await ShowWaifuCardAsync(ctx.User.Id);
            return;
        }

        var isManager = await svc.IsManagerAsync(ctx.User.Id);

        // Check if caller is a fan
        var backing = await svc.GetBackingAsync(ctx.User.Id);
        if (backing.HasValue)
        {
            var backedUser = await ctx.Client.GetUserAsync(backing.Value);
            var backedName = backedUser?.ToString() ?? backing.Value.ToString();
            var resp = Response()
                .Error(strs.waifu_fan_not_waifu(backedName))
                .Interaction(CreateOptInInteraction());
            if (isManager)
                resp = resp.Interaction(CreateWaifuListInteraction());
            await resp.SendAsync();
            return;
        }

        var eb = CreateEmbed()
            .WithOkColor()
            .WithTitle(GetText(strs.waifu_not_participating_title))
            .WithDescription(GetText(strs.waifu_not_participating(prefix)));

        var notParticipatingResp = Response()
            .Embed(eb)
            .Interaction(CreateOptInInteraction());
        if (isManager)
            notParticipatingResp = notParticipatingResp.Interaction(CreateWaifuListInteraction());
        await notParticipatingResp.SendAsync();
    }

    [Cmd]
    public Task WaifuFans([Leftover] IUser? user = null)
        => WaifuFansInternalAsync(user?.Id ?? ctx.User.Id);

    [Cmd]
    public Task WaifuFans(ulong userId)
        => WaifuFansInternalAsync(userId);

    private async Task WaifuFansInternalAsync(ulong targetUserId)
    {
        var targetUser = await ctx.Client.GetUserAsync(targetUserId);
        var targetName = targetUser?.ToString() ?? targetUserId.ToString();

        await Response()
            .Paginated()
            .PageItems(async page =>
                (IReadOnlyCollection<FanInfoDto>)await svc.GetFansAsync(targetUserId, page, 10))
            .PageSize(10)
            .Page((items, page) =>
            {
                var eb = CreateEmbed()
                    .WithOkColor()
                    .WithTitle(GetText(strs.waifu_fans_title(targetName)));

                var desc = string.Join("\n", items.Select((f, i) =>
                {
                    var rank = page * 10 + i + 1;
                    var name = f.Username ?? f.UserId.ToString();
                    return $"`{rank}.` {name}\n - `{f.UserId}`";
                }));

                return eb.WithDescription(desc);
            })
            .SendAsync();
    }

    [Cmd]
    public Task WaifuLeaderboard()
        => WaifuLeaderboardInternalAsync(WaifuLbOrder.Backing);

    [Cmd]
    public Task WaifuLeaderboard([Leftover] string order)
        => WaifuLeaderboardInternalAsync(
            order.Equals("price", StringComparison.OrdinalIgnoreCase)
                ? WaifuLbOrder.Price
                : WaifuLbOrder.Backing);

    private async Task WaifuLeaderboardInternalAsync(WaifuLbOrder order)
    {
        var currSign = cp.GetCurrencySign();
        var title = order == WaifuLbOrder.Price
            ? GetText(strs.waifu_lb_title)
            : GetText(strs.waifu_lb_title_backed);

        await Response()
            .Paginated()
            .PageItems(async page =>
                (IReadOnlyCollection<LeaderboardEntryDto>)await svc.GetLeaderboardAsync(order, page, 9))
            .PageSize(9)
            .Page((items, page) =>
            {
                var eb = CreateEmbed()
                    .WithOkColor()
                    .WithTitle(title);

                foreach (var (e, i) in items.Select((e, i) => (e, i)))
                {
                    var globalRank = page * 9 + i + 1;

                    int efficiency;
                    if (!e.HasManager)
                    {
                        efficiency = 0;
                    }
                    else
                    {
                        var statMultiplier = (e.Mood + e.Food) / 2000.0;
                        var capMultiplier = e.SnapshotTotalBacked > 0
                            ? Math.Min(1.0, e.ReturnsCap / (double)e.SnapshotTotalBacked)
                            : 1.0;
                        efficiency = (int)Math.Round(statMultiplier * capMultiplier * 100);
                    }

                    var value = $"{CurrencyHelper.N(e.Price, Culture, currSign)}\n"
                              + $"🆔 `{e.UserId}`\n"
                              + $"⚡ {efficiency}%\n"
                              + $"💰 {CurrencyHelper.N(e.SnapshotTotalBacked, Culture, currSign)}";

                    eb.AddField($"#{globalRank} {e.Username ?? GetText(strs.waifu_unknown)}", value, true);
                }

                return eb;
            })
            .SendAsync();
    }

    [Cmd]
    public async Task WaifuOptIn()
    {
        var res = await svc.OptInAsync(ctx.User.Id);

        await res.Match(
            _ => Response().Error(strs.waifu_already_opted_in).SendAsync(),
            _ => Response().Error(strs.waifu_insufficient_funds).SendAsync(),
            _ => Response()
                .Confirm(strs.waifu_opted_in(cp.GetCurrencySign()))
                .Interaction(CreateWaifuCardInteraction())
                .SendAsync()
        );
    }

    [Cmd]
    public async Task WaifuOptOut()
    {
        var res = await svc.OptOutAsync(ctx.User.Id);

        await res.Match(
            _ => Response().Error(strs.waifu_not_opted_in).SendAsync(),
            _ => Response().Error(strs.waifu_has_fans).SendAsync(),
            _ => Response().Confirm(strs.waifu_opted_out).SendAsync()
        );
    }

    [Cmd]
    public async Task WaifuBack([Leftover] IUser? user = null)
    {
        // No args = stop backing (toggle off)
        if (user is null)
        {
            var backing = await svc.GetBackingAsync(ctx.User.Id);
            if (!backing.HasValue)
            {
                await Response().Error(strs.waifu_not_backing).SendAsync();
                return;
            }

            // Confirm stop backing
            var confirmEmbed = CreateEmbed()
                .WithPendingColor()
                .WithDescription(GetText(strs.waifu_stop_backing_confirm));

            if (!await PromptUserConfirmAsync(confirmEmbed))
                return;

            var stopRes = await svc.StopBeingFanAsync(ctx.User.Id);
            await stopRes.Match(
                _ => Response().Error(strs.waifu_not_backing).SendAsync(),
                _ => Response().Confirm(strs.waifu_stopped_backing).SendAsync()
            );
            return;
        }

        // Check if already backing someone else -> switch prompt
        var currentBacking = await svc.GetBackingAsync(ctx.User.Id);
        if (currentBacking.HasValue && currentBacking.Value != user.Id)
        {
            var currentWaifu = await ctx.Client.GetUserAsync(currentBacking.Value);
            var switchEmbed = CreateEmbed()
                .WithPendingColor()
                .WithDescription(GetText(strs.waifu_switch_confirm(
                    currentWaifu?.ToString() ?? currentBacking.Value.ToString(),
                    user.ToString())));

            if (!await PromptUserConfirmAsync(switchEmbed))
                return;
        }

        var res = await svc.BecomeFanAsync(ctx.User.Id, user.Id);

        await res.Match(
            _ => Response().Error(strs.waifu_already_backing).SendAsync(),
            _ => Response().Error(strs.waifu_not_found).SendAsync(),
            _ => Response().Error(strs.waifu_self_not_allowed).SendAsync(),
            _ => Response()
                .Confirm(strs.waifu_now_backing_hint(user.ToString()))
                .Interaction(CreateBankInteraction())
                .SendAsync()
        );
    }

    [Cmd]
    public Task WaifuBuy(
        [OverrideTypeReader(typeof(BalanceTypeReader))] long amount,
        [Leftover] IUser user)
        => WaifuBuyInternalAsync(amount, user.Id, user.ToString() ?? user.Id.ToString());

    [Cmd]
    public Task WaifuBuy(
        [OverrideTypeReader(typeof(BalanceTypeReader))] long amount,
        ulong userId)
        => WaifuBuyInternalAsync(amount, userId, userId.ToString());

    private async Task WaifuBuyInternalAsync(long amount, ulong targetUserId, string targetName)
    {
        var currSign = cp.GetCurrencySign();

        var confirmEmbed = CreateEmbed()
            .WithPendingColor()
            .WithDescription(GetText(strs.waifu_buy_confirm(
                targetName,
                CurrencyHelper.N(amount, Culture, currSign))));

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var confirmBtn = _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                label: GetText(strs.waifu_btn_confirm),
                customId: "wbuy:confirm",
                style: ButtonStyle.Success),
            async smc =>
            {
                tcs.TrySetResult(true);
                await smc.DeferAsync();
            });

        await Response().Embed(confirmEmbed).Interaction(confirmBtn).SendAsync();

        var confirmed = await Task.WhenAny(tcs.Task, Task.Delay(60_000)) == tcs.Task
                        && tcs.Task.Result;

        if (!confirmed)
            return;

        var res = await svc.BuyManagerAsync(ctx.User.Id, targetUserId, amount);

        await res.Match(
            _ => Response().Error(strs.waifu_outside_buy_window).SendAsync(),
            _ => Response().Error(strs.waifu_insufficient_funds).SendAsync(),
            _ => Response().Error(strs.waifu_not_found).SendAsync(),
            _ => Response().Error(strs.waifu_price_too_low).SendAsync(),
            _ => Response().Error(strs.waifu_self_not_allowed).SendAsync(),
            info =>
            {
                var eb = CreateEmbed()
                    .WithOkColor()
                    .WithTitle(GetText(strs.waifu_bought_manager_title))
                    .WithDescription(GetText(strs.waifu_bought_manager(
                        targetName,
                        info.Value.PricePaid)))
                    .AddField(GetText(strs.waifu_goes_to_waifu), $"{info.Value.WaifuPayout:N0}", true)
                    .AddField(GetText(strs.waifu_burned), $"{info.Value.Burned:N0}", true);

                if (info.Value.OldManagerId.HasValue)
                    eb.AddField(GetText(strs.waifu_old_manager_refund), $"{info.Value.OldManagerRefund:N0}", true);

                return Response().Embed(eb).SendAsync();
            }
        );
    }

    [Cmd]
    public async Task WaifuFee(int percent)
    {
        var res = await svc.SetWaifuFeeAsync(ctx.User.Id, percent);

        await res.Match(
            _ => Response().Error(strs.waifu_not_found).SendAsync(),
            _ => Response().Error(strs.waifu_not_opted_in).SendAsync(),
            _ => Response().Error(strs.waifu_invalid_fee(1, 5)).SendAsync(),
            _ => Response().Confirm(strs.waifu_fee_set(percent)).SendAsync()
        );
    }

    private string GetActionStr(WaifuAction action, string name)
        => GetText(action switch
        {
            WaifuAction.Hug => strs.waifu_hugged(name),
            WaifuAction.Kiss => strs.waifu_kissed(name),
            WaifuAction.Pat => strs.waifu_patted(name),
            WaifuAction.Nom => strs.waifu_nommed(name),
            _ => strs.waifu_hugged(name)
        });

    private Uri? GetRandomActionGif(WaifuAction action)
    {
        var urls = action switch
        {
            WaifuAction.Hug => ic.Data.Waifu?.Hug,
            WaifuAction.Kiss => ic.Data.Waifu?.Kiss,
            WaifuAction.Pat => ic.Data.Waifu?.Pat,
            WaifuAction.Nom => ic.Data.Waifu?.Nom,
            _ => null
        };

        if (urls is null or { Length: 0 })
            return null;

        return urls[_rng.Next(0, urls.Length)];
    }

    private async Task ActionWithUsersAsync(WaifuAction action, IUser[] users)
    {
        var isMood = action != WaifuAction.Nom;
        var statName = isMood ? "mood" : "food";
        var gif = GetRandomActionGif(action);
        var targets = users.Where(u => u.Id != ctx.User.Id).Distinct().ToArray();

        if (targets.Length == 0)
        {
            await Response().Error(strs.waifu_self_not_allowed).SendAsync();
            return;
        }

        var lines = new List<string>();
        var actionsExhausted = false;

        foreach (var user in targets)
        {
            var actionText = GetActionStr(action, user.ToString()!);

            if (actionsExhausted)
            {
                lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*");
                continue;
            }

            if (isMood)
            {
                var res = await svc.ImproveMoodAsync(ctx.User.Id, user.Id, action);
                res.Switch(
                    _ => { },
                    _ => { actionsExhausted = true; lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*"); },
                    _ => lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*"),
                    _ => lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*"),
                    s => lines.Add($"{actionText} *+{s.Value} {statName}*")
                );
            }
            else
            {
                var res = await svc.ImproveFoodAsync(ctx.User.Id, user.Id);
                res.Switch(
                    _ => { },
                    _ => { actionsExhausted = true; lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*"); },
                    _ => lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*"),
                    _ => lines.Add($"{actionText} *{GetText(strs.waifu_no_effect)}*"),
                    s => lines.Add($"{actionText} *+{s.Value} {statName}*")
                );
            }
        }

        var eb = CreateEmbed()
            .WithOkColor()
            .WithDescription(string.Join("\n", lines));
        if (gif is not null)
            eb.WithImageUrl(gif.ToString());
        await Response().Embed(eb).SendAsync();
    }

    private Task ActionGifOnlyAsync(WaifuAction action)
    {
        var gif = GetRandomActionGif(action);
        var eb = CreateEmbed().WithOkColor();
        if (gif is not null)
            eb.WithImageUrl(gif.ToString());
        return Response().Embed(eb).SendAsync();
    }

    [Cmd]
    [Priority(1)]
    public Task Hug(params IUser[] users)
        => ActionWithUsersAsync(WaifuAction.Hug, users);

    [Cmd]
    [Priority(1)]
    public Task Kiss(params IUser[] users)
        => ActionWithUsersAsync(WaifuAction.Kiss, users);

    [Cmd]
    [Priority(1)]
    public Task Pat(params IUser[] users)
        => ActionWithUsersAsync(WaifuAction.Pat, users);

    [Cmd]
    [Priority(1)]
    public Task Nom(params IUser[] users)
        => ActionWithUsersAsync(WaifuAction.Nom, users);

    [Cmd]
    [Priority(0)]
    public Task Hug([Leftover] string _)
        => ActionGifOnlyAsync(WaifuAction.Hug);

    [Cmd]
    [Priority(0)]
    public Task Kiss([Leftover] string _)
        => ActionGifOnlyAsync(WaifuAction.Kiss);

    [Cmd]
    [Priority(0)]
    public Task Pat([Leftover] string _)
        => ActionGifOnlyAsync(WaifuAction.Pat);

    [Cmd]
    [Priority(0)]
    public Task Nom([Leftover] string _)
        => ActionGifOnlyAsync(WaifuAction.Nom);

    [Cmd]
    public async Task WaifuGiftShop()
    {
        var items = WaifuGiftItems.GetTodaysItems(svc.GetAllItems());
        var refreshIn = WaifuGiftItems.GetTimeUntilRefresh();
        var currSign = cp.GetCurrencySign();

        var eb = CreateEmbed()
            .WithOkColor()
            .WithTitle(GetText(strs.waifu_gift_shop_title))
            .WithFooter(GetText(strs.waifu_shop_refreshes(refreshIn.ToString(@"hh\:mm\:ss"))));

        foreach (var item in items)
        {
            var statEmoji = item.Type == GiftItemType.Food ? "🍔" : "😊";
            eb.AddField(
                $"{item.Emoji} {item.Name}",
                $"{CurrencyHelper.N(item.Price, Culture, currSign)} · +{item.Effect} {statEmoji}",
                true);
        }

        await Response().Embed(eb).SendAsync();
    }

    [Cmd]
    public async Task WaifuGift(string itemInput, [Leftover] IUser user)
    {
        var count = 1;
        var itemName = itemInput;

        var match = System.Text.RegularExpressions.Regex.Match(itemInput, @"^(\d+)[x*](.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            count = int.Parse(match.Groups[1].Value);
            itemName = match.Groups[2].Value.Trim();
        }

        var res = await svc.GiftAsync(ctx.User.Id, user.Id, itemName, count);

        await res.Match(
            _ => Response().Error(strs.waifu_self_not_allowed).SendAsync(),
            _ => Response().Error(strs.waifu_insufficient_funds).SendAsync(),
            _ => Response().Error(strs.waifu_not_found).SendAsync(),
            _ => Response().Error(strs.waifu_item_not_found(prefix)).SendAsync(),
            item => Response().Confirm(strs.waifu_gifted(
                count,
                item.Value.Emoji,
                item.Value.Name,
                user.ToString(),
                item.Value.Effect * count,
                item.Value.Type == GiftItemType.Food
                    ? GetText(strs.waifu_stat_food)
                    : GetText(strs.waifu_stat_mood)
            )).SendAsync()
        );
    }

    [Cmd]
    public Task WaifuResign([Leftover] IUser user)
        => WaifuResignInternalAsync(user.Id, user.ToString() ?? user.Id.ToString());

    [Cmd]
    public Task WaifuResign(ulong userId)
        => WaifuResignInternalAsync(userId, userId.ToString());

    private async Task WaifuResignInternalAsync(ulong targetUserId, string targetName)
    {
        var isManaging = await svc.IsManagingAsync(ctx.User.Id, targetUserId);
        if (!isManaging)
        {
            await Response().Error(strs.waifu_not_managing).SendAsync();
            return;
        }

        var confirmEmbed = CreateEmbed()
            .WithPendingColor()
            .WithTitle(GetText(strs.waifu_manager_exit_title))
            .WithDescription(GetText(strs.waifu_manager_exit_confirm(targetName)));

        if (!await PromptUserConfirmAsync(confirmEmbed))
            return;

        var res = await svc.ResignManagerAsync(ctx.User.Id, targetUserId);

        await res.Match(
            _ => Response().Error(strs.waifu_not_managing).SendAsync(),
            _ => Response().Confirm(strs.waifu_resigned_manager(targetName)).SendAsync()
        );
    }

    [Cmd]
    public Task WaifuList([Leftover] IUser? user = null)
        => WaifuListInternalAsync(user?.Id ?? ctx.User.Id);

    [Cmd]
    public Task WaifuList(ulong userId)
        => WaifuListInternalAsync(userId);

    private async Task WaifuListInternalAsync(ulong targetUserId)
    {
        var managed = await svc.GetManagedWaifusAsync(targetUserId);
        if (managed.Count == 0)
        {
            await Response().Error(strs.waifu_managing_empty).SendAsync();
            return;
        }

        var targetUser = await ctx.Client.GetUserAsync(targetUserId);
        var targetName = targetUser?.ToString() ?? targetUserId.ToString();
        var currSign = cp.GetCurrencySign();

        await Response()
            .Paginated()
            .Items(managed)
            .PageSize(10)
            .Page((items, page) =>
            {
                var eb = CreateEmbed()
                    .WithOkColor()
                    .WithTitle(GetText(strs.waifu_managing_title(targetName)));

                var desc = string.Join("\n", items.Select((m, i) =>
                {
                    var name = m.Username ?? GetText(strs.waifu_unknown);
                    var rank = page * 10 + i + 1;
                    return $"`{rank}.` `{m.WaifuUserId}` {name}\n　　{CurrencyHelper.N(m.Price, Culture, currSign)} | {CurrencyHelper.N(m.TotalBacked, Culture, currSign)} {GetText(strs.waifu_backed_label)}";
                }));

                return eb.WithDescription(desc);
            })
            .SendAsync();
    }

    [Cmd]
    public async Task WaifuPayout()
    {
        var pending = await svc.GetPendingPayoutAsync(ctx.User.Id);
        var currSign = cp.GetCurrencySign();

        if (pending < 1)
        {
            await Response().Error(strs.waifu_no_pending_payout).SendAsync();
            return;
        }

        await Response()
            .Confirm(strs.waifu_pending_payout(CurrencyHelper.N(pending, Culture, currSign)))
            .Interaction(CreateCollectPayoutConfirmInteraction())
            .SendAsync();
    }
}
}
