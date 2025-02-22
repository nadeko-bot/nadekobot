#nullable disable
namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    // [Group]
    // public partial class YtTrackCommands : NadekoModule<YtTrackService>
    // {
    //     ;
    //     [RequireContext(ContextType.Guild)]
    //     public async Task YtFollow(string ytChannelId, [Leftover] string uploadMessage = null)
    //     {
    //         var succ = await _service.ToggleChannelFollowAsync(ctx.Guild.Id, ctx.Channel.Id, ytChannelId, uploadMessage);
    //         if(succ)
    //         {
    //             await Response().Confirm(strs.yt_follow_added).SendAsync();
    //         }
    //         else
    //         {
    //             await Response().Confirm(strs.yt_follow_fail).SendAsync();
    //         }
    //     }
    //     
    //     [NadekoCommand, Usage, Description, Aliases]
    //     [RequireContext(ContextType.Guild)]
    //     public async Task YtTrackRm(int index)
    //     {
    //         //var succ = await _service.ToggleChannelTrackingAsync(ctx.Guild.Id, ctx.Channel.Id, ytChannelId, uploadMessage);
    //         //if (succ)
    //         //{
    //         //    await Response().Confirm(strs.yt_track_added).SendAsync();
    //         //}
    //         //else
    //         //{
    //         //    await Response().Confirm(strs.yt_track_fail).SendAsync();
    //         //}
    //     }
    //     
    //     [NadekoCommand, Usage, Description, Aliases]
    //     [RequireContext(ContextType.Guild)]
    //     public async Task YtTrackList()
    //     {
    //         //var succ = await _service.ToggleChannelTrackingAsync(ctx.Guild.Id, ctx.Channel.Id, ytChannelId, uploadMessage);
    //         //if (succ)
    //         //{
    //         //    await Response().Confirm(strs.yt_track_added).SendAsync();
    //         //}
    //         //else
    //         //{
    //         //    await Response().Confirm(strs.yt_track_fail).SendAsync();
    //         //}
    //     }
    // }
}