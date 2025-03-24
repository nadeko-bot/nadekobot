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
}