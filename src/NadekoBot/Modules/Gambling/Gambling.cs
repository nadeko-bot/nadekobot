#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using LinqToDB.Tools;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Services.Currency;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using NadekoBot.Modules.Gambling.Rps;
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Games;
using NadekoBot.Modules.Patronage;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling : GamblingModule<GamblingService>
{
    private readonly IGamblingService _gs;
    private readonly DbService _db;
    private readonly ICurrencyService _cs;
    private readonly DiscordSocketClient _client;
    private readonly NumberFormatInfo _enUsCulture;
    private readonly DownloadTracker _tracker;
    private readonly GamblingConfigService _configService;
    private readonly FontProvider _fonts;
    private readonly IBankService _bank;
    private readonly IRemindService _remind;
    private readonly GamblingTxTracker _gamblingTxTracker;
    private readonly IPatronageService _ps;
    private readonly RakebackService _rb;
    private readonly IBotCache _cache;
    private readonly CaptchaService _captchaService;

    public Gambling(
        IGamblingService gs,
        DbService db,
        ICurrencyService currency,
        DiscordSocketClient client,
        DownloadTracker tracker,
        GamblingConfigService configService,
        FontProvider fonts,
        IBankService bank,
        IRemindService remind,
        IPatronageService patronage,
        GamblingTxTracker gamblingTxTracker,
        RakebackService rb,
        IBotCache cache,
        CaptchaService captchaService)
        : base(configService)
    {
        _gs = gs;
        _db = db;
        _cs = currency;
        _client = client;
        _bank = bank;
        _remind = remind;
        _gamblingTxTracker = gamblingTxTracker;
        _rb = rb;
        _cache = cache;
        _captchaService = captchaService;
        _ps = patronage;
        _rng = new NadekoRandom();

        _enUsCulture = new CultureInfo("en-US", false).NumberFormat;
        _enUsCulture.NumberDecimalDigits = 0;
        _enUsCulture.NumberGroupSeparator = " ";
        _tracker = tracker;
        _configService = configService;
        _fonts = fonts;
    }

    public async Task<string> GetBalanceStringAsync(ulong userId)
    {
        var bal = await _cs.GetBalanceAsync(userId);
        return N(bal);
    }


    private async Task RemindTimelyAction(SocketMessageComponent smc, DateTime when)
    {
        var tt = TimestampTag.FromDateTime(when, TimestampTagStyles.Relative);

        await _remind.AddReminderAsync(ctx.User.Id,
            ctx.User.Id,
            ctx.Guild?.Id,
            true,
            when,
            GetText(strs.timely_time),
            ReminderType.Timely);

        await smc.RespondConfirmAsync(_sender, GetText(strs.remind_timely(tt)), ephemeral: true);
    }

    // Creates timely reminder button, parameter in hours.
    private NadekoInteractionBase CreateRemindMeInteraction(int period)
        => _inter
            .Create(ctx.User.Id,
                new ButtonBuilder(
                    label: "Remind me",
                    emote: Emoji.Parse("⏰"),
                    customId: "timely:remind_me"),
                (smc) => RemindTimelyAction(smc, DateTime.UtcNow.Add(TimeSpan.FromHours(period)))
            );

    // Creates timely reminder button, parameter in milliseconds.
    private NadekoInteractionBase CreateRemindMeInteraction(double ms)
        => _inter
            .Create(ctx.User.Id,
                new ButtonBuilder(
                    label: "Remind me",
                    emote: Emoji.Parse("⏰"),
                    customId: "timely:remind_me"),
                (smc) => RemindTimelyAction(smc, DateTime.UtcNow.Add(TimeSpan.FromMilliseconds(ms)))
            );

    private NadekoInteractionBase CreateTimelyInteraction()
        => _inter
            .Create(ctx.User.Id,
                new ButtonBuilder(
                    label: "Timely",
                    emote: Emoji.Parse("💰"),
                    customId: "timely:" + _rng.Next(123456, 999999)),
                async (smc) =>
                {
                    await smc.DeferAsync();
                    await ClaimTimely();
                });

    [Cmd]
    public async Task Timely()
    {
        var val = Config.Timely.Amount;
        var period = Config.Timely.Cooldown;
        if (val <= 0 || period <= 0)
        {
            await Response().Error(strs.timely_none).SendAsync();
            return;
        }

        if (Config.Timely.ProtType == TimelyProt.Button)
        {
            var interaction = CreateTimelyInteraction();
            var msg = await Response().Pending(strs.timely_button).Interaction(interaction).SendAsync();
            await msg.DeleteAsync();
            return;
        }
        else if (Config.Timely.ProtType == TimelyProt.Captcha)
        {
            var password = await _captchaService.GetUserCaptcha(ctx.User.Id);

            if (password is not null)
            {
                var img = _captchaService.GetPasswordImage(password);
                await using var stream = await img.ToStreamAsync();
                var toSend = Response()
                    .File(stream, "timely.png");

#if GLOBAL_NADEKO
                if (_rng.Next(0, 8) == 0)
                    toSend = toSend
                        .Text("*[Sub on Patreon](https://patreon.com/nadekobot) to remove captcha.*");
#endif

                var captchaMessage = await toSend.SendAsync();
                try
                {
                    var userInput = await GetUserInputAsync(ctx.User.Id, ctx.Channel.Id);
                    if (userInput?.ToLowerInvariant() != password?.ToLowerInvariant())
                    {
                        return;
                    }

                    await _captchaService.ClearUserCaptcha(ctx.User.Id);
                }
                finally
                {
                    _ = captchaMessage.DeleteAsync();
                }
            }
        }

        await ClaimTimely();
    }

    private async Task ClaimTimely()
    {
        var period = Config.Timely.Cooldown;
        if (await _service.ClaimTimelyAsync(ctx.User.Id, period) is { } remainder)
        {
            // Get correct time form remainder
            var interaction = CreateRemindMeInteraction(remainder.TotalMilliseconds);

            // Removes timely button if there is a timely reminder in DB
            if (_service.UserHasTimelyReminder(ctx.User.Id))
            {
                interaction = null;
            }

            var now = DateTime.UtcNow;
            var relativeTag = TimestampTag.FromDateTime(now.Add(remainder), TimestampTagStyles.Relative);
            await Response().Pending(strs.timely_already_claimed(relativeTag)).Interaction(interaction).SendAsync();
            return;
        }


        var val = Config.Timely.Amount;
        var boostGuilds = Config.BoostBonus.GuildIds ?? new();
        var guildUsers = await boostGuilds
                               .Select(async gid =>
                               {
                                   try
                                   {
                                       var guild = await _client.Rest.GetGuildAsync(gid, false);
                                       var user = await _client.Rest.GetGuildUserAsync(gid, ctx.User.Id);
                                       return (guild, user);
                                   }
                                   catch
                                   {
                                       return default;
                                   }
                               })
                               .WhenAll();

        var userInfo = guildUsers.FirstOrDefault(x => x.user?.PremiumSince is not null);
        var booster = userInfo != default;

        if (booster)
            val += Config.BoostBonus.BaseTimelyBonus;

        var patron = await _ps.GetPatronAsync(ctx.User.Id);

        var percentBonus = (_ps.PercentBonus(patron) / 100f);

        val += (int)(val * percentBonus);

        var inter = CreateRemindMeInteraction(period);

        await _cs.AddAsync(ctx.User.Id, val, new("timely", "claim"));

        var msg = GetText(strs.timely(N(val), period));
        if (booster || percentBonus > float.Epsilon)
        {
            msg += "\n\n";
            if (booster)
                msg += $"*+{N(Config.BoostBonus.BaseTimelyBonus)} bonus for boosting {userInfo.guild}!*\n";

            if (percentBonus > float.Epsilon)
                msg +=
                    $"*+{percentBonus:P0} bonus for the [Patreon](https://patreon.com/nadekobot) pledge! <:hart:746995901758832712>*";

            await Response().Confirm(msg).Interaction(inter).SendAsync();
        }
        else
            await Response().Confirm(strs.timely(N(val), period)).Interaction(inter).SendAsync();
    }

    [Cmd]
    [OwnerOnly]
    public async Task TimelyReset()
    {
        await _service.RemoveAllTimelyClaimsAsync();
        await Response().Confirm(strs.timely_reset).SendAsync();
    }

    [Cmd]
    [OwnerOnly]
    public async Task TimelySet(int amount, int period = 24)
    {
        if (amount < 0 || period < 0)
        {
            return;
        }

        _configService.ModifyConfig(gs =>
        {
            gs.Timely.Amount = amount;
            gs.Timely.Cooldown = period;
        });

        if (amount == 0)
        {
            await Response().Confirm(strs.timely_set_none).SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.timely_set(Format.Bold(N(amount)), Format.Bold(period.ToString())))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Raffle([Leftover] IRole role = null)
    {
        role ??= ctx.Guild.EveryoneRole;

        var members = (await role.GetMembersAsync()).Where(u => u.Status != UserStatus.Offline);
        var membersArray = members as IUser[] ?? members.ToArray();
        if (membersArray.Length == 0)
        {
            return;
        }

        var usr = membersArray[new NadekoRandom().Next(0, membersArray.Length)];
        await Response()
              .Confirm("🎟 " + GetText(strs.raffled_user),
                  $"**{usr.Username}**",
                  footer: $"ID: {usr.Id}")
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task RaffleAny([Leftover] IRole role = null)
    {
        role ??= ctx.Guild.EveryoneRole;

        var members = await role.GetMembersAsync();
        var membersArray = members as IUser[] ?? members.ToArray();
        if (membersArray.Length == 0)
        {
            return;
        }

        var usr = membersArray[new NadekoRandom().Next(0, membersArray.Length)];
        await Response()
              .Confirm("🎟 " + GetText(strs.raffled_user),
                  $"**{usr.Username}**",
                  footer: $"ID: {usr.Id}")
              .SendAsync();
    }

    [Cmd]
    [Priority(2)]
    public Task CurrencyTransactions(int page = 1)
        => InternalCurrencyTransactions(ctx.User.Id, page);

    [Cmd]
    [OwnerOnly]
    [Priority(0)]
    public Task CurrencyTransactions([Leftover] IUser usr)
        => InternalCurrencyTransactions(usr.Id, 1);

    [Cmd]
    [OwnerOnly]
    [Priority(-1)]
    public Task CurrencyTransactions([Leftover] ulong userId)
        => InternalCurrencyTransactions(userId, 1);

    [Cmd]
    [OwnerOnly]
    [Priority(1)]
    public Task CurrencyTransactions(IUser usr, int page)
        => InternalCurrencyTransactions(usr.Id, page);

    private async Task InternalCurrencyTransactions(ulong userId, int page)
    {
        if (--page < 0)
        {
            return;
        }

        List<CurrencyTransaction> trs;
        await using (var uow = _db.GetDbContext())
        {
            trs = await uow.Set<CurrencyTransaction>().GetPageFor(userId, page);
        }

        var embed = CreateEmbed()
                    .WithTitle(GetText(strs.transactions(
                        ((SocketGuild)ctx.Guild)?.GetUser(userId)?.ToString()
                        ?? $"{userId}")))
                    .WithOkColor();

        var sb = new StringBuilder();
        foreach (var tr in trs)
        {
            var change = tr.Amount >= 0 ? "🔵" : "🔴";
            var kwumId = new kwum(tr.Id).ToString();
            var date = $"#{Format.Code(kwumId)} `〖{GetFormattedCurtrDate(tr)}〗`";

            sb.AppendLine($"\\{change} {date} {Format.Bold(N(tr.Amount))}");
            var transactionString = GetHumanReadableTransaction(tr.Type, tr.Extra, tr.OtherId);
            if (transactionString is not null)
            {
                sb.AppendLine(transactionString);
            }

            if (!string.IsNullOrWhiteSpace(tr.Note))
            {
                sb.AppendLine($"\t`Note:` {tr.Note.TrimTo(50)}");
            }
        }

        embed.WithDescription(sb.ToString());
        embed.WithFooter(GetText(strs.page(page + 1)));
        await Response().Embed(embed).SendAsync();
    }

    private static string GetFormattedCurtrDate(CurrencyTransaction ct)
        => $"{ct.DateAdded:HH:mm yyyy-MM-dd}";

    [Cmd]
    public async Task CurrencyTransaction(kwum id)
    {
        int intId = id;
        await using var uow = _db.GetDbContext();

        var tr = await uow.Set<CurrencyTransaction>()
                          .ToLinqToDBTable()
                          .Where(x => x.Id == intId && x.UserId == ctx.User.Id)
                          .FirstOrDefaultAsync();

        if (tr is null)
        {
            await Response().Error(strs.not_found).SendAsync();
            return;
        }

        var eb = CreateEmbed().WithOkColor();

        eb.WithAuthor(ctx.User);
        eb.WithTitle(GetText(strs.transaction));
        eb.WithDescription(new kwum(tr.Id).ToString());
        eb.AddField("Amount", N(tr.Amount));
        eb.AddField("Type", tr.Type, true);
        eb.AddField("Extra", tr.Extra, true);

        if (tr.OtherId is ulong other)
        {
            eb.AddField("From Id", other);
        }

        if (!string.IsNullOrWhiteSpace(tr.Note))
        {
            eb.AddField("Note", tr.Note);
        }

        eb.WithFooter(GetFormattedCurtrDate(tr));

        await Response().Embed(eb).SendAsync();
    }

    private string GetHumanReadableTransaction(string type, string subType, ulong? maybeUserId)
        => (type, subType, maybeUserId) switch
        {
            ("gift", var name, ulong userId) => GetText(strs.curtr_gift(name, userId)),
            ("award", var name, ulong userId) => GetText(strs.curtr_award(name, userId)),
            ("take", var name, ulong userId) => GetText(strs.curtr_take(name, userId)),
            ("blackjack", _, _) => $"Blackjack - {subType}",
            ("wheel", _, _) => $"Lucky Ladder - {subType}",
            ("lula", _, _) => $"Lucky Ladder - {subType}",
            ("rps", _, _) => $"Rock Paper Scissors - {subType}",
            (null, _, _) => null,
            (_, null, _) => null,
            (_, _, ulong userId) => $"{type} - {subType} | [{userId}]",
            _ => $"{type} - {subType}"
        };

    [Cmd]
    [Priority(0)]
    public async Task Cash(ulong userId)
    {
        var cur = await GetBalanceStringAsync(userId);
        await Response().Confirm(strs.has(Format.Code(userId.ToString()), cur)).SendAsync();
    }

    private async Task BankAction(SocketMessageComponent smc)
    {
        var balance = await _bank.GetBalanceAsync(ctx.User.Id);

        await N(balance)
              .Pipe(strs.bank_balance)
              .Pipe(GetText)
              .Pipe(text => smc.RespondConfirmAsync(_sender, text, ephemeral: true));
    }

    private NadekoInteractionBase CreateCashInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                customId: "cash:bank_show_balance",
                emote: new Emoji("🏦")),
            BankAction);

    [Cmd]
    [Priority(1)]
    public async Task Cash([Leftover] IUser user = null)
    {
        user ??= ctx.User;
        var cur = await GetBalanceStringAsync(user.Id);

        var inter = user == ctx.User
            ? CreateCashInteraction()
            : null;

        await Response()
              .Confirm(
                  user.ToString()
                      .Pipe(Format.Bold)
                      .With(cur)
                      .Pipe(strs.has))
              .Interaction(inter)
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(0)]
    public async Task Give(
        [OverrideTypeReader(typeof(BalanceTypeReader))]
        long amount,
        IGuildUser receiver,
        [Leftover] string msg)
    {
        if (amount <= 0 || ctx.User.Id == receiver.Id || receiver.IsBot)
        {
            return;
        }

        if (!await _cs.TransferAsync(_sender, ctx.User, receiver, amount, msg, N(amount)))
        {
            await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
            return;
        }

        await Response().Confirm(strs.gifted(N(amount), Format.Bold(receiver.ToString()), ctx.User)).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(1)]
    public Task Give([OverrideTypeReader(typeof(BalanceTypeReader))] long amount, [Leftover] IGuildUser receiver)
        => Give(amount, receiver, null);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(0)]
    public Task Award(long amount, IGuildUser usr, [Leftover] string msg)
        => Award(amount, usr.Id, msg);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(1)]
    public Task Award(long amount, [Leftover] IGuildUser usr)
        => Award(amount, usr.Id);

    [Cmd]
    [OwnerOnly]
    [Priority(2)]
    public async Task Award(long amount, ulong usrId, [Leftover] string msg = null)
    {
        if (amount <= 0)
        {
            return;
        }

        var usr = await ((DiscordSocketClient)Context.Client).Rest.GetUserAsync(usrId);

        if (usr is null)
        {
            await Response().Error(strs.user_not_found).SendAsync();
            return;
        }

        await _cs.AddAsync(usr.Id, amount, new("award", ctx.User.ToString()!, msg, ctx.User.Id));
        await Response().Confirm(strs.awarded(N(amount), $"<@{usrId}>", ctx.User)).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(3)]
    public async Task Award(long amount, [Leftover] IRole role)
    {
        var users = (await ctx.Guild.GetUsersAsync()).Where(u => u.GetRoles().Contains(role)).ToList();

        await _cs.AddBulkAsync(users.Select(x => x.Id).ToList(),
            amount,
            new("award", ctx.User.ToString()!, role.Name, ctx.User.Id));

        await Response()
              .Confirm(strs.mass_award(N(amount),
                  Format.Bold(users.Count.ToString()),
                  Format.Bold(role.Name)))
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(0)]
    public async Task Take(long amount, [Leftover] IRole role)
    {
        var users = (await role.GetMembersAsync()).ToList();

        await _cs.RemoveBulkAsync(users.Select(x => x.Id).ToList(),
            amount,
            new("take", ctx.User.ToString()!, null, ctx.User.Id));

        await Response()
              .Confirm(strs.mass_take(N(amount),
                  Format.Bold(users.Count.ToString()),
                  Format.Bold(role.Name)))
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [OwnerOnly]
    [Priority(1)]
    public async Task Take(long amount, [Leftover] IGuildUser user)
    {
        if (amount <= 0)
        {
            return;
        }

        var extra = new TxData("take", ctx.User.ToString()!, null, ctx.User.Id);

        if (await _cs.RemoveAsync(user.Id, amount, extra))
        {
            await Response().Confirm(strs.take(N(amount), Format.Bold(user.ToString()))).SendAsync();
        }
        else
        {
            await Response()
                  .Error(strs.take_fail(N(amount), Format.Bold(user.ToString()), CurrencySign))
                  .SendAsync();
        }
    }

    [Cmd]
    [OwnerOnly]
    public async Task Take(long amount, [Leftover] ulong usrId)
    {
        if (amount <= 0)
        {
            return;
        }

        var extra = new TxData("take", ctx.User.ToString()!, null, ctx.User.Id);

        if (await _cs.RemoveAsync(usrId, amount, extra))
        {
            await Response().Confirm(strs.take(N(amount), $"<@{usrId}>")).SendAsync();
        }
        else
        {
            await Response()
                  .Error(strs.take_fail(N(amount), Format.Code(usrId.ToString()), CurrencySign))
                  .SendAsync();
        }
    }

    [Cmd]
    public async Task BetRoll([OverrideTypeReader(typeof(BalanceTypeReader))] long amount)
    {
        if (!await CheckBetMandatory(amount))
        {
            return;
        }

        var maybeResult = await _gs.BetRollAsync(ctx.User.Id, amount);
        if (!maybeResult.TryPickT0(out var result, out _))
        {
            await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
            return;
        }


        var win = (long)result.Won;
        string str;
        if (win > 0)
        {
            str = GetText(strs.betroll_win(result.Threshold + (result.Roll == 100 ? " 👑" : "")));
        }
        else
        {
            str = GetText(strs.better_luck);
        }

        var eb = CreateEmbed()
                 .WithAuthor(ctx.User)
                 .WithDescription(Format.Bold(str))
                 .AddField(GetText(strs.roll2), result.Roll.ToString(CultureInfo.InvariantCulture), true)
                 .AddField(GetText(strs.bet), N(amount), true)
                 .AddField(GetText(strs.won), N((long)result.Won), true)
                 .WithOkColor();

        await Response().Embed(eb).SendAsync();
    }

    [Cmd]
    [NadekoOptions<LbOpts>]
    [Priority(0)]
    public Task Leaderboard(params string[] args)
        => Leaderboard(1, args);

    [Cmd]
    [NadekoOptions<LbOpts>]
    [Priority(1)]
    public async Task Leaderboard(int page = 1, params string[] args)
    {
        if (--page < 0)
        {
            return;
        }

        var (opts, _) = OptionsParser.ParseFrom(new LbOpts(), args);

        if (ctx.Guild is null)
        {
            opts.Clean = false;
        }


        async Task<IReadOnlyCollection<DiscordUser>> GetTopRichest(int curPage)
        {
            if (opts.Clean)
            {
                await ctx.Channel.TriggerTypingAsync();
                await _tracker.EnsureUsersDownloadedAsync(ctx.Guild);

                var users = ((SocketGuild)ctx.Guild).Users.Map(x => x.Id);
                var perPage = 9;

                await using var uow = _db.GetDbContext();
                var cleanRichest = await uow.GetTable<DiscordUser>()
                                            .Where(x => x.UserId.In(users))
                                            .OrderByDescending(x => x.CurrencyAmount)
                                            .Skip(curPage * perPage)
                                            .Take(perPage)
                                            .ToListAsync();

                return cleanRichest;
            }
            else
            {
                await using var uow = _db.GetDbContext();
                return await uow.Set<DiscordUser>().GetTopRichest(_client.CurrentUser.Id, curPage);
            }
        }

        await Response()
              .Paginated()
              .PageItems(GetTopRichest)
              .PageSize(9)
              .CurrentPage(page)
              .Page((toSend, curPage) =>
              {
                  var embed = CreateEmbed()
                              .WithOkColor()
                              .WithTitle(CurrencySign + " " + GetText(strs.leaderboard));

                  if (!toSend.Any())
                  {
                      embed.WithDescription(GetText(strs.no_user_on_this_page));
                      return Task.FromResult(embed);
                  }

                  for (var i = 0; i < toSend.Count; i++)
                  {
                      var x = toSend[i];
                      var usrStr = x.ToString().TrimTo(20, true);

                      var j = i;
                      embed.AddField("#" + ((9 * curPage) + j + 1) + " " + usrStr, N(x.CurrencyAmount), true);
                  }

                  return Task.FromResult(embed);
              })
              .SendAsync();
    }

    public enum InputRpsPick : byte
    {
        R = 0,
        Rock = 0,
        Rocket = 0,
        P = 1,
        Paper = 1,
        Paperclip = 1,
        S = 2,
        Scissors = 2
    }

    [Cmd]
    public async Task Rps(InputRpsPick pick, [OverrideTypeReader(typeof(BalanceTypeReader))] long amount = default)
    {
        static string GetRpsPick(InputRpsPick p)
        {
            switch (p)
            {
                case InputRpsPick.R:
                    return "🚀";
                case InputRpsPick.P:
                    return "📎";
                default:
                    return "✂️";
            }
        }

        if (!await CheckBetOptional(amount) || amount == 1)
            return;

        var res = await _gs.RpsAsync(ctx.User.Id, amount, (byte)pick);

        if (!res.TryPickT0(out var result, out _))
        {
            await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
            return;
        }

        var embed = CreateEmbed();

        string msg;
        if (result.Result == RpsResultType.Draw)
        {
            msg = GetText(strs.rps_draw(GetRpsPick(pick)));
        }
        else if (result.Result == RpsResultType.Win)
        {
            msg = GetText(strs.rps_win(ctx.User.Mention,
                GetRpsPick(pick),
                GetRpsPick((InputRpsPick)result.ComputerPick)));
        }
        else
        {
            msg = GetText(strs.rps_win(ctx.Client.CurrentUser.Mention,
                GetRpsPick((InputRpsPick)result.ComputerPick),
                GetRpsPick(pick)));
        }

        embed
            .WithOkColor()
            .WithDescription(msg);

        if (amount > 0)
        {
            embed
                .AddField(GetText(strs.bet), N(amount), true)
                .AddField(GetText(strs.won), $"{N((long)result.Won)}", true);
        }

        await Response().Embed(embed).SendAsync();
    }

    private static readonly ImmutableArray<string> _emojis =
        new[] { "⬆", "↖", "⬅", "↙", "⬇", "↘", "➡", "↗" }.ToImmutableArray();

    private readonly NadekoRandom _rng;


    [Cmd]
    public async Task LuckyLadder([OverrideTypeReader(typeof(BalanceTypeReader))] long amount)
    {
        if (!await CheckBetMandatory(amount))
            return;

        var res = await _gs.LulaAsync(ctx.User.Id, amount);
        if (!res.TryPickT0(out var result, out _))
        {
            await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
            return;
        }

        var multis = result.Multipliers;

        var sb = new StringBuilder();
        foreach (var multi in multis)
        {
            sb.Append($"╠══╣");

            if (multi == result.Multiplier)
                sb.Append($"{Format.Bold($"x{multi:0.##}")} ⬅️");
            else
                sb.Append($"||x{multi:0.##}||");

            sb.AppendLine();
        }

        var eb = CreateEmbed()
                 .WithOkColor()
                 .WithDescription(sb.ToString())
                 .AddField(GetText(strs.bet), N(amount), true)
                 .AddField(GetText(strs.won), $"{N((long)result.Won)}", true)
                 .WithAuthor(ctx.User);


        await Response().Embed(eb).SendAsync();
    }


    public enum GambleTestTarget
    {
        Slot,
        Betroll,
        Betflip,
        BetflipT,
        BetDraw,
        BetDrawHL,
        BetDrawRB,
        Lula,
        Rps,
    }

    [Cmd]
    [OwnerOnly]
    public async Task BetTest()
    {
        var values = Enum.GetValues<GambleTestTarget>()
                         .Select(x => $"`{x}`")
                         .Join(", ");

        await Response().Confirm(GetText(strs.available_tests), values).SendAsync();
    }

    [Cmd]
    [OwnerOnly]
    public async Task BetTest(GambleTestTarget target, int tests = 1000)
    {
        if (tests <= 0)
            return;

        await ctx.Channel.TriggerTypingAsync();

        var streak = 0;
        var maxW = 0;
        var maxL = 0;

        var dict = new Dictionary<decimal, int>();
        for (var i = 0; i < tests; i++)
        {
            var multi = target switch
            {
                GambleTestTarget.BetDraw => (await _gs.BetDrawAsync(ctx.User.Id, 0, 1, 0)).AsT0.Multiplier,
                GambleTestTarget.BetDrawRB => (await _gs.BetDrawAsync(ctx.User.Id, 0, null, 1)).AsT0.Multiplier,
                GambleTestTarget.BetDrawHL => (await _gs.BetDrawAsync(ctx.User.Id, 0, 0, null)).AsT0.Multiplier,
                GambleTestTarget.Slot => (await _gs.SlotAsync(ctx.User.Id, 0)).AsT0.Multiplier,
                GambleTestTarget.Betflip => (await _gs.BetFlipAsync(ctx.User.Id, 0, 0)).AsT0.Multiplier,
                GambleTestTarget.BetflipT => (await _gs.BetFlipAsync(ctx.User.Id, 0, 1)).AsT0.Multiplier,
                GambleTestTarget.Lula => (await _gs.LulaAsync(ctx.User.Id, 0)).AsT0.Multiplier,
                GambleTestTarget.Rps => (await _gs.RpsAsync(ctx.User.Id, 0, (byte)(i % 3))).AsT0.Multiplier,
                GambleTestTarget.Betroll => (await _gs.BetRollAsync(ctx.User.Id, 0)).AsT0.Multiplier,
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };

            if (dict.ContainsKey(multi))
                dict[multi] += 1;
            else
                dict.Add(multi, 1);

            if (multi < 1)
            {
                if (streak <= 0)
                    --streak;
                else
                    streak = -1;

                maxL = Math.Max(maxL, -streak);
            }
            else if (multi > 1)
            {
                if (streak >= 0)
                    ++streak;
                else
                    streak = 1;

                maxW = Math.Max(maxW, streak);
            }
        }

        var sb = new StringBuilder();
        decimal payout = 0;
        foreach (var key in dict.Keys.OrderByDescending(x => x))
        {
            sb.AppendLine($"x**{key}** occured `{dict[key]}` times. {dict[key] * 1.0f / tests * 100}%");
            payout += key * dict[key];
        }

        sb.AppendLine();
        sb.AppendLine($"Longest win streak: `{maxW}`");
        sb.AppendLine($"Longest lose streak: `{maxL}`");

        await Response()
              .Confirm(GetText(strs.test_results_for(target)),
                  sb.ToString(),
                  footer: $"Total Bet: {tests} | Payout: {payout:F0} | {payout * 1.0M / tests * 100}%")
              .SendAsync();
    }

    private NadekoInteractionBase CreateRakebackInteraction()
        => _inter.Create(ctx.User.Id,
            new ButtonBuilder(
                customId: "cash:rakeback",
                emote: new Emoji("💸")),
            RakebackAction);

    private async Task RakebackAction(SocketMessageComponent arg)
    {
        var rb = await _rb.ClaimRakebackAsync(ctx.User.Id);

        if (rb == 0)
        {
            await arg.DeferAsync();
            return;
        }

        await arg.RespondAsync(_sender, GetText(strs.rakeback_claimed(N(rb))), MsgType.Ok);
    }

    [Cmd]
    public async Task Rakeback()
    {
        var rb = await _rb.GetRakebackAsync(ctx.User.Id);

        if (rb < 1)
        {
            await Response()
                  .Error(strs.rakeback_none)
                  .SendAsync();

            return;
        }

        var inter = CreateRakebackInteraction();
        await Response()
              .Pending(strs.rakeback_available(N(rb)))
              .Interaction(inter)
              .SendAsync();
    }
}