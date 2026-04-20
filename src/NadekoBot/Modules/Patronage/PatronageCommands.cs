using System.Text;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Help;

public partial class Help
{
    [OnlyPublicBot]
    public partial class Patronage : NadekoModule
    {
        private readonly PatronageService _service;
        private readonly PatronageConfig _pConf;

        public Patronage(PatronageService service, PatronageConfig pConf)
        {
            _service = service;
            _pConf = pConf;
        }

        [Cmd]
        [Priority(2)]
        public Task Patron()
            => InternalPatron(ctx.User);

        [Cmd]
        [Priority(0)]
        [OwnerOnly]
        public Task Patron(IUser user)
            => InternalPatron(user);

        [Cmd]
        [OwnerOnly]
        public async Task Patrons(int page = 1)
        {
            if (--page < 0)
                return;

            if (!_pConf.Data.IsEnabled)
            {
                await Response().Error(strs.patron_not_enabled).SendAsync();
                return;
            }

            var grouped = await _service.GetActivePatronsByTierAsync();

            if (grouped.Count == 0)
            {
                await Response().Error(strs.patrons_none).SendAsync();
                return;
            }

            const int perPage = 10;
            var pages = BuildPatronPages(grouped, perPage);

            await Response()
                .Paginated()
                .Items(pages)
                .PageSize(1)
                .CurrentPage(page)
                .Page((items, _) =>
                {
                    var sb = new StringBuilder();
                    foreach (var entry in items[0])
                    {
                        if (entry.IsHeader)
                        {
                            sb.AppendLine($"**{entry.Tier.ToFullName()}**");
                        }
                        else
                        {
                            var name = entry.Username ?? entry.UserId.ToString();
                            sb.AppendLine($"**{name}**");
                            sb.AppendLine($"\U0001f194 `{entry.UserId}`");
                        }
                    }

                    var eb = CreateEmbed()
                        .WithOkColor()
                        .WithTitle(GetText(strs.patrons_title))
                        .WithDescription(sb.ToString());

                    return Task.FromResult(eb);
                })
                .SendAsync();
        }

        private static List<List<PatronEntry>> BuildPatronPages(
            IReadOnlyList<(PatronTier Tier, IReadOnlyList<(ulong UserId, string? Username)> Patrons)> grouped,
            int perPage)
        {
            var pages = new List<List<PatronEntry>>();
            var cur = new List<PatronEntry>();
            var count = 0;

            foreach (var (tier, patrons) in grouped)
            {
                if (patrons.Count == 0)
                    continue;

                cur.Add(PatronEntry.Header(tier));

                foreach (var (userId, username) in patrons)
                {
                    if (count == perPage)
                    {
                        pages.Add(cur);
                        cur = [PatronEntry.Header(tier)];
                        count = 0;
                    }

                    cur.Add(PatronEntry.Row(tier, userId, username));
                    count++;
                }
            }

            if (cur.Count > 0)
                pages.Add(cur);

            return pages;
        }

        private readonly record struct PatronEntry(
            PatronTier Tier,
            bool IsHeader,
            ulong UserId,
            string? Username)
        {
            public static PatronEntry Header(PatronTier tier)
                => new(tier, true, 0, null);

            public static PatronEntry Row(PatronTier tier, ulong userId, string? username)
                => new(tier, false, userId, username);
        }

        [Cmd]
        [Priority(0)]
        [OwnerOnly]
        public async Task PatronMessage(PatronTier tierAndHigher, string message)
        {
            _ = ctx.Channel.TriggerTypingAsync();
            var result = await _service.SendMessageToPatronsAsync(tierAndHigher, message);

            await Response()
                .Confirm(strs.patron_msg_sent(
                    Format.Code(tierAndHigher.ToString()),
                    Format.Bold(result.Success.ToString()),
                    Format.Bold(result.Failed.ToString())))
                .SendAsync();
        }

        // [OwnerOnly]
        // public async Task PatronGift(IUser user, int amount)
        // {
        //     // i can't figure out a good way to gift more than one month at the moment.
        //
        //     if (amount < 1)
        //         return;
        //     
        //     var patron = _service.GiftPatronAsync(user, amount);
        //
        //     var eb = CreateEmbed();
        //
        //     await Response().Embed(eb.WithDescription($"Added **{days}** days of Patron benefits to {user.Mention}!")
        //                                    .AddField("Tier", Format.Bold(patron.Tier.ToString()), true)
        //                                    .AddField("Amount", $"**{patron.Amount / 100.0f:N1}$**", true)
        //                                    .AddField("Until", TimestampTag.FromDateTime(patron.ValidThru.AddDays(1)))).SendAsync();
        //     
        //
        // }

        private async Task InternalPatron(IUser user)
        {
            if (!_pConf.Data.IsEnabled)
            {
                await Response().Error(strs.patron_not_enabled).SendAsync();
                return;
            }

            var maybePatron = await _service.GetPatronAsync(user.Id);

            var eb = CreateEmbed()
                .WithAuthor(user)
                .WithTitle(GetText(strs.patron_info))
                .WithOkColor();

            if (maybePatron is not { } patron)
            {
                eb.WithDescription("You don't have an active subscription");
            }
            else
            {
                eb.AddField(GetText(strs.tier), Format.Bold(patron.Tier.ToFullName()), true)
                    .AddField(GetText(strs.pledge), $"**{patron.Amount / 100.0f:N1}$**", true);

                if (patron.Tier != PatronTier.None)
                    eb.AddField(GetText(strs.expires),
                        patron.ValidThru.AddDays(1).ToShortAndRelativeTimestampTag(),
                        true);
            }


            try
            {
                await Response().User(ctx.User).Embed(eb).SendAsync();
                _ = ctx.OkAsync();
            }
            catch
            {
                await Response().Error(strs.cant_dm).SendAsync();
            }
        }
    }
}