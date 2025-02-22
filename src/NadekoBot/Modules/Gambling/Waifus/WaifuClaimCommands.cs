#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Waifu;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Db.Models;
using System.Globalization;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class WaifuClaimCommands : GamblingModule<WaifuService>
    {
        public WaifuClaimCommands(GamblingConfigService gamblingConfService)
            : base(gamblingConfService)
        {
        }

        [Cmd]
        public async Task WaifuReset()
        {
            var price = _service.GetResetPrice(ctx.User);
            var embed = CreateEmbed()
                               .WithTitle(GetText(strs.waifu_reset_confirm))
                               .WithDescription(GetText(strs.waifu_reset_price(Format.Bold(N(price)))));

            if (!await PromptUserConfirmAsync(embed))
                return;

            if (await _service.TryReset(ctx.User))
            {
                await Response().Confirm(strs.waifu_reset).SendAsync();
                return;
            }

            await Response().Error(strs.waifu_reset_fail).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task WaifuClaims()
        {
            await Response()
                  .Paginated()
                  .PageItems(async (page) => await _service.GetClaimsAsync(ctx.User.Id, page))
                  .Page((items, page) =>
                  {
                      var eb = CreateEmbed()
                                      .WithOkColor()
                                      .WithTitle("Waifus");

                      if (items.Count == 0)
                      {
                          eb
                              .WithPendingColor()
                              .WithDescription(GetText(strs.empty_page));

                          return eb;
                      }

                      for (var i = 0; i < items.Count; i++)
                      {
                          var item = items[i];
                          eb.AddField($"`#{(page * 9) + 1 + i}`  {N(item.Price)}",
                              $"""
                               {item.Username}
                               ||{item.UserId}||
                               """,
                              true
                          );
                      }

                      return eb;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task WaifuClaim(long amount, [Leftover] IUser target)
        {
            if (amount < Config.Waifu.MinPrice)
            {
                await Response().Error(strs.waifu_isnt_cheap(Config.Waifu.MinPrice + CurrencySign)).SendAsync();
                return;
            }

            if (target.Id == ctx.User.Id)
            {
                await Response().Error(strs.waifu_not_yourself).SendAsync();
                return;
            }

            var (w, isAffinity, result) = await _service.ClaimWaifuAsync(ctx.User, target, amount);

            if (result == WaifuClaimResult.InsufficientAmount)
            {
                await Response()
                      .Error(
                          strs.waifu_not_enough(N((long)Math.Ceiling(w.Price * (isAffinity ? 0.88f : 1.1f)))))
                      .SendAsync();
                return;
            }

            if (result == WaifuClaimResult.NotEnoughFunds)
            {
                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                return;
            }

            var msg = GetText(strs.waifu_claimed(
                Format.Bold(ctx.User.ToString()),
                Format.Bold(target.ToString()),
                N(amount)));

            if (w.Affinity?.UserId == ctx.User.Id)
                msg += "\n" + GetText(strs.waifu_fulfilled(target, N(w.Price)));
            else
                msg = " " + msg;
            await Response().Confirm(ctx.User.Mention + msg).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public async Task WaifuTransfer(ulong waifuId, IUser newOwner)
        {
            if (!await _service.WaifuTransfer(ctx.User, waifuId, newOwner))
            {
                await Response().Error(strs.waifu_transfer_fail).SendAsync();
                return;
            }

            await Response()
                  .Confirm(strs.waifu_transfer_success(Format.Bold(waifuId.ToString()),
                      Format.Bold(ctx.User.ToString()),
                      Format.Bold(newOwner.ToString())))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public async Task WaifuTransfer(IUser waifu, IUser newOwner)
        {
            if (!await _service.WaifuTransfer(ctx.User, waifu.Id, newOwner))
            {
                await Response().Error(strs.waifu_transfer_fail).SendAsync();
                return;
            }

            await Response()
                  .Confirm(strs.waifu_transfer_success(Format.Bold(waifu.ToString()),
                      Format.Bold(ctx.User.ToString()),
                      Format.Bold(newOwner.ToString())))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(-1)]
        public Task Divorce([Leftover] string target)
        {
            var waifuUserId = _service.GetWaifuUserId(ctx.User.Id, target);
            if (waifuUserId == default)
                return Response().Error(strs.waifu_not_yours).SendAsync();

            return Divorce(waifuUserId);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public Task Divorce([Leftover] IGuildUser target)
            => Divorce(target.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public async Task Divorce([Leftover] ulong targetId)
        {
            if (targetId == ctx.User.Id)
                return;

            var (w, result, amount) = await _service.DivorceWaifuAsync(ctx.User, targetId);

            if (result == DivorceResult.SucessWithPenalty)
            {
                await Response()
                      .Confirm(strs.waifu_divorced_like(Format.Bold(w.Waifu.ToString()),
                          N(amount)))
                      .SendAsync();
            }
            else if (result == DivorceResult.Success)
                await Response().Confirm(strs.waifu_divorced_notlike(N(amount))).SendAsync();
            else if (result == DivorceResult.NotYourWife)
                await Response().Error(strs.waifu_not_yours).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Affinity([Leftover] IGuildUser user = null)
        {
            if (user?.Id == ctx.User.Id)
            {
                await Response().Error(strs.waifu_egomaniac).SendAsync();
                return;
            }

            var (oldAff, sucess, remaining) = await _service.ChangeAffinityAsync(ctx.User, user);
            if (!sucess)
            {
                if (remaining is not null)
                {
                    await Response()
                          .Error(strs.waifu_affinity_cooldown(
                              Format.Bold(((int)remaining?.TotalHours).ToString()),
                              Format.Bold(remaining?.Minutes.ToString())))
                          .SendAsync();
                }
                else
                    await Response().Error(strs.waifu_affinity_already).SendAsync();

                return;
            }

            if (user is null)
            {
                await Response().Confirm(strs.waifu_affinity_reset).SendAsync();
            }
            else if (oldAff is null)
            {
                await Response()
                      .Confirm(strs.waifu_affinity_set(Format.Bold(ctx.User.ToString()), Format.Bold(user.ToString())))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.waifu_affinity_changed(
                          Format.Bold(ctx.User.ToString()),
                          Format.Bold(oldAff.ToString()),
                          Format.Bold(user.ToString())))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task WaifuLb(int page = 1)
        {
            page--;

            if (page < 0)
                return;

            if (page > 100)
                page = 100;

            var waifus = await _service.GetTopWaifusAtPage(page);

            if (waifus.Count == 0)
            {
                await Response().Confirm(strs.waifus_none).SendAsync();
                return;
            }

            var embed = CreateEmbed().WithTitle(GetText(strs.waifus_top_waifus)).WithOkColor();

            var i = 0;
            foreach (var w in waifus)
            {
                var j = i++;
                embed.AddField("#" + ((page * 9) + j + 1) + " - " + N(w.Price), GetLbString(w));
            }

            await Response().Embed(embed).SendAsync();
        }

        private string GetLbString(WaifuLbResult w)
        {
            var claimer = "no one";
            string status;

            var waifuUsername = w.WaifuName.TrimTo(20);
            var claimerUsername = w.ClaimerName?.TrimTo(20);

            if (w.ClaimerName is not null)
                claimer = $"{claimerUsername}";
            if (w.Affinity is null)
                status = $"... but {waifuUsername}'s heart is empty";
            else if (w.Affinity == w.ClaimerName)
                status = $"... and {waifuUsername} likes {claimerUsername} too <3";
            else
                status = $"... but {waifuUsername}'s heart belongs to {w.Affinity.TrimTo(20)}";
            return $"**{waifuUsername}** - claimed by **{claimer}**\n\t{status}";
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public Task WaifuInfo([Leftover] IUser target = null)
        {
            if (target is null)
                target = ctx.User;

            return InternalWaifuInfo(target.Id, target.ToString());
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public Task WaifuInfo(ulong targetId)
            => InternalWaifuInfo(targetId);

        private async Task InternalWaifuInfo(ulong targetId, string name = null)
        {
            var wi = await _service.GetFullWaifuInfoAsync(targetId);
            var affInfo = _service.GetAffinityTitle(wi.AffinityCount);

            var waifuItems = _service.GetWaifuItems().ToDictionary(x => x.ItemEmoji, x => x);

            var nobody = GetText(strs.nobody);
            var itemList = await _service.GetItems(wi.WaifuId);
            var itemsStr = !itemList.Any()
                ? "-"
                : string.Join("\n",
                    itemList.Where(x => waifuItems.TryGetValue(x.ItemEmoji, out _))
                            .OrderByDescending(x => waifuItems[x.ItemEmoji].Price)
                            .GroupBy(x => x.ItemEmoji)
                            .Take(60)
                            .Select(x => $"{x.Key} x{x.Count(),-3}")
                            .Chunk(2)
                            .Select(x => string.Join(" ", x)));

            var claimsNames = (await _service.GetClaimNames(wi.WaifuId));
            var claimsStr = claimsNames
                            .Shuffle()
                            .Take(30)
                            .Join('\n');

            var fansList = await _service.GetFansNames(wi.WaifuId);
            var fansStr = fansList
                          .Shuffle()
                          .Take(30)
                          .Select((x) => claimsNames.Contains(x) ? $"{x} 💞" : x)
                          .Join('\n');

            if (string.IsNullOrWhiteSpace(fansStr))
                fansStr = "-";

            var embed = CreateEmbed()
                               .WithOkColor()
                               .WithTitle(GetText(strs.waifu)
                                          + " "
                                          + (wi.FullName ?? name ?? targetId.ToString())
                                          + " - \"the "
                                          + _service.GetClaimTitle(wi.ClaimCount)
                                          + "\"")
                               .AddField(GetText(strs.price), N(wi.Price), true)
                               .AddField(GetText(strs.claimed_by), wi.ClaimerName ?? nobody, true)
                               .AddField(GetText(strs.likes), wi.AffinityName ?? nobody, true)
                               .AddField(GetText(strs.changes_of_heart),
                                   $"{wi.AffinityCount} - \"the {affInfo}\"",
                                   true)
                               .AddField(GetText(strs.divorces), wi.DivorceCount.ToString(), true)
                               .AddField("\u200B", "\u200B", true)
                               .AddField(GetText(strs.fans(fansList.Count)), fansStr, true)
                               .AddField($"Waifus ({wi.ClaimCount})",
                                   wi.ClaimCount == 0 ? nobody : claimsStr,
                                   true)
                               .AddField(GetText(strs.gifts), itemsStr, true);

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public async Task WaifuGift(int page = 1)
        {
            if (--page < 0 || page > (Config.Waifu.Items.Count - 1) / 9)
                return;

            var waifuItems = _service.GetWaifuItems();
            await Response()
                  .Paginated()
                  .Items(waifuItems.OrderBy(x => x.Negative)
                                   .ThenBy(x => x.Price)
                                   .ToList())
                  .PageSize(9)
                  .CurrentPage(page)
                  .Page((items, _) =>
                  {
                      var embed = CreateEmbed().WithTitle(GetText(strs.waifu_gift_shop)).WithOkColor();

                      items
                          .ToList()
                          .ForEach(x => embed.AddField(
                              $"{(!x.Negative ? string.Empty : "\\💔")} {x.ItemEmoji} {x.Name}",
                              Format.Bold(N(x.Price)),
                              true));

                      return embed;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public async Task WaifuGift(MultipleWaifuItems items, [Leftover] IUser waifu)
        {
            if (waifu.Id == ctx.User.Id)
                return;

            var sucess = await _service.GiftWaifuAsync(ctx.User, waifu, items.Item, items.Count);

            if (sucess)
            {
                await Response()
                      .Confirm(strs.waifu_gift(
                          Format.Bold($"{GetCountString(items)}{items.Item} {items.Item.ItemEmoji}"),
                          Format.Bold(waifu.ToString())))
                      .SendAsync();
            }
            else
                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
        }

        private static string GetCountString(MultipleWaifuItems items)
            => items.Count > 1
                ? $"{items.Count}x "
                : string.Empty;
    }
}