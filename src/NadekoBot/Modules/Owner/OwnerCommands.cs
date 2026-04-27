using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Owner;

[OwnerOnly]
public partial class Owner(VoteRewardService vrs, IPatronageService ps) : NadekoModule
{
    [Cmd]
    public async Task VoteFeed()
    {
        vrs.SetVoiceChannel(ctx.Channel);
        await ctx.OkAsync();
    }

    [Cmd]
    public async Task PatronAdd(long cents, ulong userId)
    {
        if (!ps.GetConfig().IsEnabled)
        {
            await Response().Error(strs.patron_not_enabled).SendAsync();
            return;
        }

        if (cents <= 0)
        {
            await Response().Error(strs.patron_add_invalid_amount).SendAsync();
            return;
        }

        var maybePatron = await ps.AddManualPatronAsync(userId, cents);
        if (maybePatron is not { } patron)
        {
            await Response().Error(strs.patron_add_failed).SendAsync();
            return;
        }

        var eb = CreateEmbed()
            .WithOkColor()
            .WithTitle(GetText(strs.patron_added))
            .AddField(GetText(strs.tier), Format.Bold(patron.Tier.ToFullName()), true)
            .AddField(GetText(strs.pledge), $"**{patron.Amount / 100.0f:N1}$**", true)
            .AddField(GetText(strs.expires),
                patron.ValidThru.AddDays(1).ToShortAndRelativeTimestampTag(),
                true);

        await Response().Embed(eb).SendAsync();
    }

    private static CancellationTokenSource? _cts = null;

    [Cmd]
    public async Task MassPing()
    {
        if (_cts is { } t)
        {
            await t.CancelAsync();
        }
        _cts = new();

        try
        {
            var users = await ctx.Guild.GetUsersAsync().Pipe(u => u.Where(x => !x.IsBot).ToArray());

            var currentIndex = 0;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var batch = users[currentIndex..(currentIndex += 50)];

                    var mentions = batch.Select(x => x.Mention).Join(" ");
                    var msg = await ctx.Channel.SendMessageAsync(mentions, allowedMentions: AllowedMentions.All);
                    msg.DeleteAfter(3);
                }
                catch
                {
                    // ignored
                }

                await Task.Delay(2500);
            }
        }
        finally
        {
            _cts = null;
        }
    }
}