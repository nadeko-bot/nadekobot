using NadekoBot.Modules.Games.Fish.Db;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    public class FishItemCommands(FishItemService fis, ICurrencyProvider cp) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishShop()
        {
            var items = fis.GetItems();

            await Response()
                .Paginated()
                .Items(items)
                .PageSize(9)
                .CurrentPage(0)
                .Page((pageItems, i) =>
                {
                    var eb = CreateEmbed()
                        .WithTitle(GetText(strs.fish_items_title))
                        .WithFooter("`.fibuy <id>` to by an item")
                        .WithOkColor();

                    foreach (var item in pageItems)
                    {
                        var description = GetItemDescription(item);
                        eb.AddField($"{item.Id}",
                            $"""
                             {description}
                              
                             """,
                            true);
                    }

                    return eb;
                })
                .AddFooter(false)
                .SendAsync();
        }

        private string GetItemDescription(FishItem item, UserFishItem? userItem = null)
        {
            var multiplierInfo = GetMultiplierInfo(item);

            var priceText = userItem is null
                ? $"【 **{CurrencyHelper.N(item.Price, Culture, cp.GetCurrencySign())}** 】"
                : "";

            return $"""
                     《 **{item.Name}** 》
                     {GetEmoji(item.ItemType)} `{item.ItemType.ToString().ToLower()}` {priceText}
                     {item.Description}
                     {GetItemNotes(item, userItem)}
                     {multiplierInfo}
                    """;
        }

        private string GetItemNotes(FishItem item, UserFishItem? userItem)
        {
            var stats = new List<string>();

            if (item.Uses.HasValue)
                stats.Add($"**Uses:** {userItem?.UsesLeft ?? item.Uses}");

            if (item.DurationMinutes.HasValue)
                stats.Add($"**Duration:** {userItem?.ExpiryFromNowInMinutes() ?? item.DurationMinutes}m");

            var toReturn = stats.Count > 0 ? string.Join(" | ", stats) + "\n" : "\n";

            return "\n" + toReturn;
        }

        public static string GetEmoji(FishItemType itemType)
            => itemType switch
            {
                FishItemType.Pole => @"\🎣",
                FishItemType.Boat => @"\⛵",
                FishItemType.Bait => @"\🍥",
                FishItemType.Potion => @"\🍷",
                _ => ""
            };

        private string GetMultiplierInfo(FishItem item)
        {
            var multipliers = new FishMultipliers()
            {
                FishMultiplier = item.FishMultiplier ?? 1,
                TrashMultiplier = item.TrashMultiplier ?? 1,
                RareMultiplier = item.RareMultiplier ?? 1,
                StarMultiplier = item.MaxStarMultiplier ?? 1,
                FishingSpeedMultiplier = item.FishingSpeedMultiplier ?? 1
            };

            return GetMultiplierInfo(multipliers);
        }


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
            var res = await fis.BuyItemAsync(ctx.User.Id, itemId);

            if (res.TryPickT1(out var err, out var eqItem))
            {
                if (err == BuyResult.InsufficientFunds)
                    await Response().Error(strs.not_enough(cp.GetCurrencySign())).SendAsync();
                else
                    await Response().Error(strs.fish_item_not_found).SendAsync();

                return;
            }

            var embed = CreateEmbed()
                .WithDescription(GetText(strs.fish_buy_success))
                .AddField(eqItem.Name, GetMultiplierInfo(eqItem));

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
            var eqItem = await fis.EquipItemAsync(ctx.User.Id, index);

            if (eqItem is null)
            {
                await Response().Error(strs.fish_item_not_found).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                .WithDescription(GetText(strs.fish_use_success))
                .AddField(eqItem.Name, GetMultiplierInfo(eqItem));

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
                .PageSize(9)
                .Page((items, page) =>
                {
                    page += 1;
                    var eb = CreateEmbed()
                        .WithAuthor(ctx.User)
                        .WithTitle(GetText(strs.fish_inv_title))
                        .WithFooter($"`.fiuse <num>` to use/equip an item")
                        .WithOkColor();

                    for (var i = 0; i < items.Count; i++)
                    {
                        var (userItem, item) = items[i];
                        var isEquipped = userItem.IsEquipped;

                        if (item is null)
                        {
                            eb.AddField($"{(page * 9) + i + 1} | Item not found", $"ID: {userItem.Id}", true);
                            continue;
                        }

                        var description = GetItemDescription(item, userItem);

                        if (isEquipped)
                            description = "🫴 **IN USE**\n" + description;

                        eb.AddField($"{i + 1} | {item.Name} ",
                            $"""
                             {description}
                             """,
                            true);
                    }

                    return eb;
                })
                .AddFooter(false)
                .SendAsync();
        }
    }
}