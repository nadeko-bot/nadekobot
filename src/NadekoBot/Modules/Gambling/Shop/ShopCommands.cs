#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using LinqToDB.EntityFrameworkCore;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class ShopCommands : GamblingModule<IShopService>
    {
        public enum List
        {
            List
        }

        public enum Role
        {
            Role
        }

        public enum Command
        {
            Command,
            Cmd
        }

        private readonly DbService _db;
        private readonly ICurrencyService _cs;
        private readonly NadekoRandom _rng;

        public ShopCommands(DbService db, ICurrencyService cs, GamblingConfigService gamblingConf)
            : base(gamblingConf)
        {
            _db = db;
            _cs = cs;
            _rng = new NadekoRandom();
        }

        private async Task ShopInternalAsync(int page = 0)
        {
            if (page < 0)
                throw new ArgumentOutOfRangeException(nameof(page));

            await using var uow = _db.GetDbContext();
            var entries = await uow.Set<ShopEntry>()
                .Where(x => x.GuildId == ctx.Guild.Id)
                .Include(x => x.Items)
                .ToListAsyncEF();

            await Response()
                   .Paginated()
                   .Items(entries.ToList())
                   .PageSize(9)
                   .CurrentPage(page)
                   .Page((items, curPage) =>
                   {
                       if (!items.Any())
                           return CreateEmbed().WithErrorColor().WithDescription(GetText(strs.shop_none));
                       var embed = CreateEmbed().WithOkColor().WithTitle(GetText(strs.shop));

                       for (var i = 0; i < items.Count; i++)
                       {
                           var entry = items[i];
                           embed.AddField($"#{(curPage * 9) + i + 1} - {N(entry.Price)}",
                               EntryToString(entry),
                               true);
                       }

                       return embed;
                   })
                   .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task Shop(int page = 1)
        {
            if (--page < 0)
                return Task.CompletedTask;

            return ShopInternalAsync(page);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Buy(int index)
        {
            index -= 1;
            if (index < 0)
                return;
            ShopEntry entry;
            await using (var uow = _db.GetDbContext())
            {
                entry = await uow.Set<ShopEntry>()
                    .Where(x => x.GuildId == ctx.Guild.Id)
                    .Include(x => x.Items)
                    .OrderBy(x => x.Id)
                    .Skip(index)
                    .FirstOrDefaultAsync();
            }


            if (entry is null)
            {
                await Response().Error(strs.shop_item_not_found).SendAsync();
                return;
            }

            if (entry.RoleRequirement is ulong reqRoleId)
            {
                var role = ctx.Guild.GetRole(reqRoleId);
                if (role is null)
                {
                    await Response().Error(strs.shop_item_req_role_not_found).SendAsync();
                    return;
                }

                var guser = (IGuildUser)ctx.User;
                if (!guser.RoleIds.Contains(reqRoleId))
                {
                    await Response()
                          .Error(strs.shop_item_req_role_unfulfilled(Format.Bold(role.ToString())))
                          .SendAsync();
                    return;
                }
            }

            if (entry.Type == ShopEntryType.Role)
            {
                var guser = (IGuildUser)ctx.User;
                var role = ctx.Guild.GetRole(entry.RoleId);

                if (role is null)
                {
                    await Response().Error(strs.shop_role_not_found).SendAsync();
                    return;
                }

                if (guser.RoleIds.Any(id => id == role.Id))
                {
                    await Response().Error(strs.shop_role_already_bought).SendAsync();
                    return;
                }

                if (await _cs.RemoveAsync(ctx.User.Id, entry.Price, new("shop", "buy", entry.Type.ToString())))
                {
                    try
                    {
                        await guser.AddRoleAsync(role);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error adding shop role");
                        await _cs.AddAsync(ctx.User.Id, entry.Price, new("shop", "error-refund"));
                        await Response().Error(strs.shop_role_purchase_error).SendAsync();
                        return;
                    }

                    var profit = GetProfitAmount(entry.Price);
                    await _cs.AddAsync(entry.AuthorId, profit, new("shop", "sell", $"Shop sell item - {entry.Type}"));
                    await _cs.AddAsync(ctx.Client.CurrentUser.Id, entry.Price - profit, new("shop", "cut"));
                    await Response().Confirm(strs.shop_role_purchase(Format.Bold(role.Name))).SendAsync();
                    return;
                }

                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                return;
            }

            else if (entry.Type == ShopEntryType.List)
            {
                if (entry.Items.Count == 0)
                {
                    await Response().Error(strs.out_of_stock).SendAsync();
                    return;
                }

                var item = entry.Items.ToArray()[_rng.Next(0, entry.Items.Count)];

                if (await _cs.RemoveAsync(ctx.User.Id, entry.Price, new("shop", "buy", entry.Type.ToString())))
                {
                    await using (var uow = _db.GetDbContext())
                    {
                        uow.Set<ShopEntryItem>().Remove(item);
                        await uow.SaveChangesAsync();
                    }

                    try
                    {
                        await Response()
                              .User(ctx.User)
                              .Embed(CreateEmbed()
                                     .WithOkColor()
                                     .WithTitle(GetText(strs.shop_purchase(ctx.Guild.Name)))
                                     .AddField(GetText(strs.item), item.Text)
                                     .AddField(GetText(strs.price), entry.Price.ToString(), true)
                                     .AddField(GetText(strs.name), entry.Name, true))
                              .SendAsync();

                        await _cs.AddAsync(entry.AuthorId,
                            GetProfitAmount(entry.Price),
                            new("shop", "sell", entry.Name));
                    }
                    catch
                    {
                        await _cs.AddAsync(ctx.User.Id, entry.Price, new("shop", "error-refund", entry.Name));
                        await using (var uow = _db.GetDbContext())
                        {
                            var entries = new IndexedCollection<ShopEntry>(await uow.Set<ShopEntry>()
                                                                                  .Where(x => x.GuildId == ctx.Guild.Id)
                                                                                  .Include(x => x.Items)
                                                                                  .ToListAsyncEF());
                            entry = entries.ElementAtOrDefault(index);
                            if (entry is not null)
                            {
                                if (entry.Items.Add(item))
                                    await uow.SaveChangesAsync();
                            }
                        }

                        await Response().Error(strs.shop_buy_error).SendAsync();
                        return;
                    }

                    await Response().Confirm(strs.shop_item_purchase).SendAsync();
                }
                else
                {
                    await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                }
            }
            else if (entry.Type == ShopEntryType.Command)
            {
                var guild = ctx.Guild as SocketGuild;
                var channel = ctx.Channel as ISocketMessageChannel;
                var msg = ctx.Message as SocketUserMessage;
                var user = await ctx.Guild.GetUserAsync(entry.AuthorId);

                if (guild is null || channel is null || msg is null || user is null)
                {
                    await Response().Error(strs.shop_command_invalid_context).SendAsync();
                    return;
                }

                if (!await _cs.RemoveAsync(ctx.User.Id, entry.Price, new("shop", "buy", entry.Type.ToString())))
                {
                    await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                    return;
                }
                else
                {
                    var buyer = (IGuildUser)ctx.User;
                    var cmd = entry.Command
                                   .Replace("%you%", buyer.Mention)
                                   .Replace("%you.mention%", buyer.Mention)
                                   .Replace("%you.username%", buyer.Username)
                                   .Replace("%you.name%", buyer.GlobalName ?? buyer.Username)
                                   .Replace("%you.nick%", buyer.DisplayName);

                    var eb = CreateEmbed()
                             .WithPendingColor()
                             .WithTitle("Executing shop command")
                             .WithDescription(cmd);

                    var msgTask = Response().Embed(eb).SendAsync();

                    await _cs.AddAsync(entry.AuthorId,
                        GetProfitAmount(entry.Price),
                        new("shop", "sell", entry.Name));

                    await Task.Delay(250);
                    await _cmdHandler.TryRunCommand(guild,
                        channel,
                        new DoAsUserMessage(
                            msg,
                            user,
                            cmd
                        ));

                    try
                    {
                        var pendingMsg = await msgTask;
                        await pendingMsg.EditAsync(
                            SmartEmbedText.FromEmbed(eb
                                                     .WithOkColor()
                                                     .WithTitle("Shop command executed")
                                                     .Build()));
                    }
                    catch
                    {
                    }
                }
            }
        }

        private long GetProfitAmount(int price)
            => (int)Math.Ceiling((1.0m - Config.BotCuts.ShopSaleCut) * price);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task ShopAdd(Command _, int price, [Leftover] string command)
        {
            if (price < 1)
                return;


            var entry = await _service.AddShopCommandAsync(ctx.Guild.Id, ctx.User.Id, price, command);

            await Response().Embed(EntryToEmbed(entry).WithTitle(GetText(strs.shop_item_add))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task ShopAdd(Role _, int price, [Leftover] IRole role)
        {
            if (price < 1)
                return;

            var entry = new ShopEntry
            {
                Name = "-",
                Price = price,
                Type = ShopEntryType.Role,
                AuthorId = ctx.User.Id,
                RoleId = role.Id,
                RoleName = role.Name
            };
            await using (var uow = _db.GetDbContext())
            {
                var entries = new IndexedCollection<ShopEntry>(await uow.Set<ShopEntry>()
                    .Where(x => x.GuildId == ctx.Guild.Id)
                    .Include(x => x.Items)
                    .ToListAsyncEF())
                {
                    entry
                };
                await uow.SaveChangesAsync();
            }

            await Response().Embed(EntryToEmbed(entry).WithTitle(GetText(strs.shop_item_add))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopAdd(List _, int price, [Leftover] string name)
        {
            if (price < 1)
                return;

            var entry = new ShopEntry
            {
                Name = name.TrimTo(100),
                Price = price,
                Type = ShopEntryType.List,
                AuthorId = ctx.User.Id,
                Items = new()
            };
            await using (var uow = _db.GetDbContext())
            {
                var entries = await uow.Set<ShopEntry>()
                    .Where(x => x.GuildId == ctx.Guild.Id)
                    .ToListAsyncEF();

                var indexed = new IndexedCollection<ShopEntry>(entries);
                indexed.Add(entry);

                uow.Add(entry);
                await uow.SaveChangesAsync();

            }

            await Response().Embed(EntryToEmbed(entry).WithTitle(GetText(strs.shop_item_add))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopListAdd(int index, [Leftover] string itemText)
        {
            index -= 1;
            if (index < 0)
                return;
            var item = new ShopEntryItem
            {
                Text = itemText
            };
            ShopEntry entry;
            var rightType = false;
            var added = false;
            await using (var uow = _db.GetDbContext())
            {
                var entries = await uow.Set<ShopEntry>()
                    .Where(x => x.GuildId == ctx.Guild.Id)
                    .Include(x => x.Items)
                    .ToListAsyncEF();

                var indexed = new IndexedCollection<ShopEntry>(entries);
                entry = indexed.ElementAtOrDefault(index);
                if (entry is not null && (rightType = entry.Type == ShopEntryType.List))
                {
                    if (entry.Items.Add(item))
                    {
                        added = true;
                        uow.SaveChanges();
                    }
                }
            }

            if (entry is null)
                await Response().Error(strs.shop_item_not_found).SendAsync();
            else if (!rightType)
                await Response().Error(strs.shop_item_wrong_type).SendAsync();
            else if (added == false)
                await Response().Error(strs.shop_list_item_not_unique).SendAsync();
            else
                await Response().Confirm(strs.shop_list_item_added).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopRemove(int index)
        {
            index -= 1;
            if (index < 0)
                return;
            ShopEntry removed;
            await using (var uow = _db.GetDbContext())
            {
                var items = await uow.Set<ShopEntry>()
                    .Where(x => x.GuildId == ctx.Guild.Id)
                    .Include(x => x.Items)
                    .ToListAsyncLinqToDB();

                var entries = new IndexedCollection<ShopEntry>(items);
                removed = entries.ElementAtOrDefault(index);
                if (removed is not null)
                {
                    uow.RemoveRange(removed.Items);
                    uow.Remove(removed);
                    uow.SaveChanges();
                }
            }

            if (removed is null)
                await Response().Error(strs.shop_item_not_found).SendAsync();
            else
                await Response().Embed(EntryToEmbed(removed).WithTitle(GetText(strs.shop_item_rm))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopChangePrice(int index, int price)
        {
            if (--index < 0 || price <= 0)
                return;

            var succ = await _service.ChangeEntryPriceAsync(ctx.Guild.Id, index, price);
            if (succ)
            {
                await ShopInternalAsync(index / 9);
                await ctx.OkAsync();
            }
            else
                await ctx.ErrorAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopChangeName(int index, [Leftover] string newName)
        {
            if (--index < 0 || string.IsNullOrWhiteSpace(newName))
                return;

            var succ = await _service.ChangeEntryNameAsync(ctx.Guild.Id, index, newName);
            if (succ)
            {
                await ShopInternalAsync(index / 9);
                await ctx.OkAsync();
            }
            else
                await ctx.ErrorAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopSwap(int index1, int index2)
        {
            if (--index1 < 0 || --index2 < 0 || index1 == index2)
                return;

            var succ = await _service.SwapEntriesAsync(ctx.Guild.Id, index1, index2);
            if (succ)
            {
                await ShopInternalAsync(index1 / 9);
                await ctx.OkAsync();
            }
            else
                await ctx.ErrorAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopMove(int fromIndex, int toIndex)
        {
            if (--fromIndex < 0 || --toIndex < 0 || fromIndex == toIndex)
                return;

            var succ = await _service.MoveEntryAsync(ctx.Guild.Id, fromIndex, toIndex);
            if (succ)
            {
                await ShopInternalAsync(toIndex / 9);
                await ctx.OkAsync();
            }
            else
                await ctx.ErrorAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ShopReq(int itemIndex, [Leftover] IRole role = null)
        {
            if (--itemIndex < 0)
                return;

            var succ = await _service.SetItemRoleRequirementAsync(ctx.Guild.Id, itemIndex, role?.Id);
            if (!succ)
            {
                await Response().Error(strs.shop_item_not_found).SendAsync();
                return;
            }

            if (role is null)
                await Response().Confirm(strs.shop_item_role_no_req(itemIndex)).SendAsync();
            else
                await Response().Confirm(strs.shop_item_role_req(itemIndex + 1, role)).SendAsync();
        }

        public EmbedBuilder EntryToEmbed(ShopEntry entry)
        {
            var embed = CreateEmbed().WithOkColor();

            if (entry.Type == ShopEntryType.Role)
            {
                return embed
                       .AddField(GetText(strs.name),
                           GetText(strs.shop_role(Format.Bold(ctx.Guild.GetRole(entry.RoleId)?.Name
                                                              ?? "MISSING_ROLE"))),
                           true)
                       .AddField(GetText(strs.price), N(entry.Price), true)
                       .AddField(GetText(strs.type), entry.Type.ToString(), true);
            }

            if (entry.Type == ShopEntryType.List)
            {
                return embed.AddField(GetText(strs.name), entry.Name, true)
                            .AddField(GetText(strs.price), N(entry.Price), true)
                            .AddField(GetText(strs.type), GetText(strs.random_unique_item), true);
            }

            else if (entry.Type == ShopEntryType.Command)
            {
                return embed
                       .AddField(GetText(strs.name), Format.Code(entry.Command), true)
                       .AddField(GetText(strs.price), N(entry.Price), true)
                       .AddField(GetText(strs.type), entry.Type.ToString(), true);
            }

            //else if (entry.Type == ShopEntryType.Infinite_List)
            //    return embed.AddField(GetText(strs.name), GetText(strs.shop_role(Format.Bold(entry.RoleName)), true))
            //            .AddField(GetText(strs.price), entry.Price.ToString(), true)
            //            .AddField(GetText(strs.type), entry.Type.ToString(), true);
            return null;
        }

        public string EntryToString(ShopEntry entry)
        {
            var prepend = string.Empty;
            if (entry.RoleRequirement is not null)
                prepend = Format.Italics(GetText(strs.shop_item_requires_role($"<@&{entry.RoleRequirement}>")))
                          + Environment.NewLine;

            if (entry.Type == ShopEntryType.Role)
                return prepend
                       + GetText(strs.shop_role(Format.Bold(ctx.Guild.GetRole(entry.RoleId)?.Name ?? "MISSING_ROLE")));
            if (entry.Type == ShopEntryType.List)
                return prepend + GetText(strs.unique_items_left(entry.Items.Count)) + "\n" + entry.Name;

            if (entry.Type == ShopEntryType.Command)
                return prepend + Format.Code(entry.Command);
            return prepend;
        }
    }
}