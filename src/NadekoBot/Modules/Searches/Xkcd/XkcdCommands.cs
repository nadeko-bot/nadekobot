namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class XkcdCommands(XkcdService svc) : NadekoModule
    {
        private static readonly TimeSpan _altDelay = TimeSpan.FromSeconds(10);

        [Cmd]
        [Priority(0)]
        public async Task Xkcd(string? arg = null)
        {
            if (arg is null)
            {
                await SendComicAsync(await svc.GetRandomAsync());
                return;
            }

            if (!arg.AsSpan().Trim().Equals("latest", StringComparison.InvariantCultureIgnoreCase))
            {
                await Response().Error(strs.comic_not_found).SendAsync();
                return;
            }

            await SendComicAsync(await svc.GetLatestAsync());
        }

        [Cmd]
        [Priority(1)]
        public async Task Xkcd(int num)
            => await SendComicAsync(await svc.GetComicAsync(num));

        private async Task SendComicAsync(XkcdComic? comic)
        {
            if (comic is null)
            {
                await Response().Error(strs.comic_not_found).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                .WithOkColor()
                .WithImageUrl(comic.ImageLink)
                .WithAuthor(comic.Title, "https://xkcd.com/s/919f27.ico", $"{XkcdService.XKCD_URL}/{comic.Num}")
                .AddField(GetText(strs.comic_number), comic.Num.ToString(), true)
                .AddField(GetText(strs.date),
                    $"{comic.Year}-{comic.Month.PadLeft(2, '0')}-{comic.Day.PadLeft(2, '0')}",
                    true);

            var sent = await Response().Embed(embed).SendAsync();

            if (string.IsNullOrWhiteSpace(comic.Alt))
                return;

            await Task.Delay(_altDelay);

            try
            {
                await sent.ModifyAsync(m => m.Embed = embed.AddField("Alt", comic.Alt.TrimTo(EmbedFieldBuilder.MaxFieldValueLength)).Build());
            }
            catch (HttpException)
            {
                // the message was deleted or the bot lost access
            }
        }
    }
}
