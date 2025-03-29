using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Owner;

[OwnerOnly]
public class Owner(VoteRewardService vrs) : NadekoModule
{
    [Cmd]
    public async Task VoteFeed()
    {
        vrs.SetVoiceChannel(ctx.Channel);
        await ctx.OkAsync();
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
            var users = await ctx.Guild.GetUsersAsync().Fmap(u => u.Where(x => !x.IsBot).ToArray());

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