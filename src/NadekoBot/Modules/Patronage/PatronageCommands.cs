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

            var quotaStats = await _service.LimitStats(user.Id);

            var eb = CreateEmbed()
                            .WithAuthor(user)
                            .WithTitle(GetText(strs.patron_info))
                            .WithOkColor();

            if (quotaStats.Count == 0 || maybePatron is not { } patron)
            {
                eb.WithDescription(GetText(strs.no_quota_found));
            }
            else
            {
                eb.AddField(GetText(strs.tier), Format.Bold(patron.Tier.ToFullName()), true)
                  .AddField(GetText(strs.pledge), $"**{patron.Amount / 100.0f:N1}$**", true);

                if (patron.Tier != PatronTier.None)
                    eb.AddField(GetText(strs.expires),
                        patron.ValidThru.AddDays(1).ToShortAndRelativeTimestampTag(),
                        true);

                eb.AddField(GetText(strs.quotas), "⁣", false);

                var text = GetQuotaList(quotaStats);
                if (!string.IsNullOrWhiteSpace(text))
                    eb.AddField(GetText(strs.modules), text, true);
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

        private string GetQuotaList(
            IReadOnlyDictionary<LimitedFeatureName, (int Cur, QuotaLimit Quota)> featureQuotaStats)
        {
            var text = string.Empty;
            foreach (var (key, (cur, quota)) in featureQuotaStats)
            {
                text += $"\n⁣\t`{key}`\n";
                if (quota.QuotaPeriod == QuotaPer.PerHour)
                    text += $"⁣ ⁣  {cur}/{(quota.Quota == -1 ? "∞" : quota.Quota)} {QuotaPeriodToString(quota.QuotaPeriod)}\n";
            }

            return text;
        }

        public string QuotaPeriodToString(QuotaPer per)
            => per switch
            {
                QuotaPer.PerHour => "per hour",
                QuotaPer.PerDay => "per day",
                QuotaPer.PerMonth => "per month",
                QuotaPer.Total => "total",
                _ => throw new ArgumentOutOfRangeException(nameof(per), per, null)
            };
    }
}