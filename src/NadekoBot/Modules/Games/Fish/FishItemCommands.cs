using NadekoBot.Modules.Games.Fish;
using NadekoBot.Modules.Games.Fish.Db;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    public class FishItemCommands(FishItemService fis, ICurrencyProvider cp, FishService fs) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishShop()
        {
            var items = fis.GetItems();
            var sign = cp.GetCurrencySign();

            await Response()
                .Paginated()
                .Items(items)
                .PageSize(5)
                .CurrentPage(0)
                .Page((pageItems, _) =>
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in pageItems)
                    {
                        sb.AppendLine(GetShopItemDescription(item, sign));
                        sb.AppendLine();
                    }

                    return CreateEmbed()
                        .WithTitle(GetText(strs.fish_items_title))
                        .WithDescription(sb.ToString().TrimEnd())
                        .WithFooter("Use .fibuy <id> to purchase")
                        .WithOkColor();
                })
                .AddFooter(false)
                .SendAsync();
        }

        private string GetShopItemDescription(FishItem item, string sign)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"**#{item.Id} \u2500 {item.Name}** {GetEmoji(item.ItemType)}");

            sb.Append($"> *{item.Description}*\n");

            var statsLine = new List<string> { $"\U0001f4b0 **{CurrencyHelper.N(item.Price, Culture, sign)}**" };
            statsLine.Add($"`{item.ItemType.ToString().ToLower()}`");
            if (item.LevelReq.HasValue)
                statsLine.Add($"\U0001f9e0 Lv.{item.LevelReq}+");

            var notes = GetCompactNotes(item);
            if (notes.Length > 0)
                statsLine.Add(notes);

            sb.Append($"> {string.Join(" \u00b7 ", statsLine)}");

            var mults = GetInlineMultipliers(item);
            if (mults.Length > 0)
                sb.Append($"\n> {mults}");

            return sb.ToString();
        }

        private string GetInvItemDescription(FishItem item, UserFishItem userItem)
        {
            var sb = new System.Text.StringBuilder();

            if (userItem.IsEquipped)
                sb.AppendLine("\U0001faf4 **IN USE**");

            sb.Append($"> *{item.Description}*\n");

            var statsLine = new List<string> { $"`{item.ItemType.ToString().ToLower()}`" };

            if (item.Uses.HasValue)
                statsLine.Add($"{userItem.UsesLeft ?? item.Uses} uses left");
            if (item.DurationMinutes.HasValue)
                statsLine.Add($"{userItem.ExpiryFromNowInMinutes() ?? item.DurationMinutes}m");

            sb.Append($"> {string.Join(" \u00b7 ", statsLine)}");

            var mults = GetInlineMultipliers(item);
            if (mults.Length > 0)
                sb.Append($"\n> {mults}");

            return sb.ToString();
        }

        private static string GetCompactNotes(FishItem item)
        {
            var parts = new List<string>();
            if (item.Uses.HasValue)
                parts.Add($"{item.Uses} uses");
            if (item.DurationMinutes.HasValue)
                parts.Add($"{item.DurationMinutes}m");
            return string.Join(" \u00b7 ", parts);
        }

        private string GetInlineMultipliers(FishItem item)
        {
            var parts = new List<string>();
            if (item.FishMultiplier is not null and not 1.0d)
                parts.Add($"{AsPercent(item.FishMultiplier.Value)} fish");
            if (item.TrashMultiplier is not null and not 1.0d)
                parts.Add($"{AsPercent(item.TrashMultiplier.Value)} trash");
            if (item.RareMultiplier is not null and not 1.0d)
                parts.Add($"{AsPercent(item.RareMultiplier.Value)} rare");
            if (item.MaxStarMultiplier is not null and not 1.0d)
                parts.Add($"{AsPercent(item.MaxStarMultiplier.Value)} stars");
            if (item.FishingSpeedMultiplier is not null and not 1.0d)
                parts.Add($"{AsPercent(item.FishingSpeedMultiplier.Value)} speed");
            return string.Join(" \u00b7 ", parts);
        }

        public static string GetEmoji(FishItemType itemType)
            => itemType switch
            {
                FishItemType.Pole => "\U0001f3a3",
                FishItemType.Boat => "\u26f5",
                FishItemType.Bait => "\U0001f365",
                FishItemType.Potion => "\U0001f377",
                FishItemType.SpotCoin => "\U0001fa99",
                _ => ""
            };

        public static string GetMultiplierInfo(FishMultipliers item)
        {
            var multipliers = new List<string>();
            if (item.FishMultiplier is not 1.0d)
                multipliers.Add($"{AsPercent(item.FishMultiplier)} chance to catch fish");

            if (item.TrashMultiplier is not 1.0d)
                multipliers.Add($"{AsPercent(item.TrashMultiplier)} chance to catch trash");

            if (item.RareMultiplier is not 1.0d)
                multipliers.Add($"{AsPercent(item.RareMultiplier)} chance to catch rare fish");

            if (item.StarMultiplier is not 1.0d)
                multipliers.Add($"{AsPercent(item.StarMultiplier)} to max star rating");

            if (item.FishingSpeedMultiplier is not 1.0d)
                multipliers.Add($"{AsPercent(item.FishingSpeedMultiplier)} fishing speed");

            return multipliers.Count > 0
                ? $"{string.Join("\n", multipliers)}\n"
                : "";
        }

        private static string AsPercent(double multiplier)
        {
            var percentage = (int)((multiplier - 1.0f) * 100);
            return percentage >= 0 ? $"**+{percentage}%**" : $"**{percentage}%**";
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishBuy(int itemId)
        {
            var (skill, _) = await fs.GetSkill(ctx.User.Id);
            var res = await fis.BuyItemAsync(ctx.User.Id, itemId, skill);

            if (res.TryPickT1(out var err, out var eqItem))
            {
                if (err == BuyResult.InsufficientFunds)
                    await Response().Error(strs.not_enough(cp.GetCurrencySign())).SendAsync();
                else if (err == BuyResult.InsufficientLevel)
                {
                    var item = fis.GetItem(itemId);
                    await Response().Error(strs.fish_level_too_low(item!.LevelReq!.Value)).SendAsync();
                }
                else
                    await Response().Error(strs.fish_item_not_found).SendAsync();

                return;
            }

            var buyFieldValue = $"*{eqItem.Description}*";
            var buyMultInfo = GetMultiplierInfo(new FishMultipliers
            {
                FishMultiplier = eqItem.FishMultiplier ?? 1,
                TrashMultiplier = eqItem.TrashMultiplier ?? 1,
                RareMultiplier = eqItem.RareMultiplier ?? 1,
                StarMultiplier = eqItem.MaxStarMultiplier ?? 1,
                FishingSpeedMultiplier = eqItem.FishingSpeedMultiplier ?? 1
            });
            if (!string.IsNullOrWhiteSpace(buyMultInfo))
                buyFieldValue += "\n" + buyMultInfo;

            var embed = CreateEmbed()
                .WithDescription(GetText(strs.fish_buy_success))
                .AddField(eqItem.Name, buyFieldValue);

            await Response()
                .Embed(embed)
                .Interaction(_inter.Create(ctx.User.Id,
                    new ButtonBuilder("Inventory", Guid.NewGuid().ToString(), ButtonStyle.Secondary),
                    (smc) => FishInv()))
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishUse(int index)
        {
            var userItems = await fis.GetUserItemsAsync(ctx.User.Id);
            if (index < 1 || index > userItems.Count)
            {
                await Response().Error(strs.fish_item_not_found).SendAsync();
                return;
            }

            var (userItem, fishItem) = userItems[index - 1];
            if (fishItem is null)
            {
                await Response().Error(strs.fish_item_not_found).SendAsync();
                return;
            }

            if (fishItem.ItemType == FishItemType.SpotCoin)
            {
                var result = await fis.UseSpotCoinAsync(ctx.User.Id, ctx.Channel.Id);

                if (result == UseSpotCoinResult.NotOwned)
                {
                    await Response().Error(strs.fish_spot_coin_none).SendAsync();
                    return;
                }

                if (result is UseSpotCoinResult.Success success)
                {
                    await Response()
                        .Confirm(strs.fish_spot_changed(success.NewSpot.ToString()))
                        .SendAsync();
                }

                return;
            }

            var eqItem = await fis.EquipItemAsync(ctx.User.Id, index);

            if (eqItem is null)
            {
                await Response().Error(strs.fish_item_not_found).SendAsync();
                return;
            }

            var useFieldValue = $"*{eqItem.Description}*";
            var useMultInfo = GetMultiplierInfo(new FishMultipliers
            {
                FishMultiplier = eqItem.FishMultiplier ?? 1,
                TrashMultiplier = eqItem.TrashMultiplier ?? 1,
                RareMultiplier = eqItem.RareMultiplier ?? 1,
                StarMultiplier = eqItem.MaxStarMultiplier ?? 1,
                FishingSpeedMultiplier = eqItem.FishingSpeedMultiplier ?? 1
            });
            if (!string.IsNullOrWhiteSpace(useMultInfo))
                useFieldValue += "\n" + useMultInfo;

            var embed = CreateEmbed()
                .WithDescription(GetText(strs.fish_use_success))
                .AddField(eqItem.Name, useFieldValue);

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishUnequip(FishItemType itemType)
        {
            var res = await fis.UnequipItemAsync(ctx.User.Id, itemType);

            if (res == UnequipResult.Success)
                await Response().Confirm(strs.fish_unequip_success).SendAsync();
            else if (res == UnequipResult.NotFound)
                await Response().Error(strs.fish_item_not_found).SendAsync();
            else
                await Response().Error(strs.fish_cant_uneq_potion).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishInv()
        {
            var userItems = await fis.GetUserItemsAsync(ctx.User.Id);

            await Response()
                .Paginated()
                .Items(userItems)
                .PageSize(5)
                .Page((items, page) =>
                {
                    var sb = new System.Text.StringBuilder();
                    for (var i = 0; i < items.Count; i++)
                    {
                        var (userItem, item) = items[i];
                        var idx = (page * 5) + i + 1;

                        if (item is null)
                        {
                            sb.AppendLine($"**#{idx} \u2500 ???**");
                            sb.AppendLine($"> Item not found (ID: {userItem.ItemId})");
                        }
                        else
                        {
                            sb.AppendLine($"**#{idx} \u2500 {item.Name}** {GetEmoji(item.ItemType)}");
                            sb.Append(GetInvItemDescription(item, userItem));
                        }

                        sb.AppendLine();
                    }

                    return CreateEmbed()
                        .WithAuthor(ctx.User)
                        .WithTitle(GetText(strs.fish_inv_title))
                        .WithDescription(sb.ToString().TrimEnd())
                        .WithFooter("Use .fiuse <num> to equip an item")
                        .WithOkColor();
                })
                .AddFooter(false)
                .SendAsync();
        }
    }
}