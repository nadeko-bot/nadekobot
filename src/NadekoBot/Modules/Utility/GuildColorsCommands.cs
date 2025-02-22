using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group("sclr")]
    public class GuildColorsCommands : NadekoModule<IGuildColorsService>
    {
        [Cmd]
        [UserPerm(GuildPerm.ManageGuild)]
        [RequireContext(ContextType.Guild)]
        public async Task ServerColorsShow()
        {
            var colors = _service.GetColors(ctx.Guild.Id);
            var okHex = colors?.Ok?.RawValue.ToString("x6");
            var warnHex = colors?.Warn?.RawValue.ToString("x6");
            var errHex = colors?.Error?.RawValue.ToString("x6");
            EmbedBuilder[] ebs =
            [
                CreateEmbed()
                    .WithOkColor()
                    .WithDescription($"\\✅ {okHex}"),
                CreateEmbed()
                    .WithPendingColor()
                    .WithDescription($"\\⏳\\⚠️ {warnHex}"),
                CreateEmbed()
                    .WithErrorColor()
                    .WithDescription($"\\❌ {errHex}")
            ];

            await Response()
                  .Embeds(ebs)
                  .SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageGuild)]
        [RequireContext(ContextType.Guild)]
        public async Task ServerColorOk([Leftover] Rgba32? color = null)
        {
            await _service.SetOkColor(ctx.Guild.Id, color);

            await Response().Confirm(strs.server_color_set).SendAsync();
            await ServerColorsShow();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageGuild)]
        [RequireContext(ContextType.Guild)]
        public async Task ServerColorPending([Leftover] Rgba32? color = null)
        {
            await _service.SetPendingColor(ctx.Guild.Id, color);

            await Response().Confirm(strs.server_color_set).SendAsync();
            await ServerColorsShow();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageGuild)]
        [RequireContext(ContextType.Guild)]
        public async Task ServerColorError([Leftover] Rgba32? color = null)
        {
            await _service.SetErrorColor(ctx.Guild.Id, color);

            await Response().Confirm(strs.server_color_set).SendAsync();
            await ServerColorsShow();
        }
    }
}