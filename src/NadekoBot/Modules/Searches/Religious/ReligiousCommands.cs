namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    public partial class ReligiousCommands : NadekoModule<ReligiousApiService>
    {
        private readonly IHttpClientFactory _httpFactory;

        public ReligiousCommands(IHttpClientFactory httpFactory)
            => _httpFactory = httpFactory;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Bible(string book, string chapterAndVerse)
        {
            var res = await _service.GetBibleVerseAsync(book, chapterAndVerse);

            if (!res.TryPickT0(out var verse, out var error))
            {
                await Response().Error(error.Value).SendAsync();
                return;
            }

            await Response()
                  .Embed(CreateEmbed()
                                .WithOkColor()
                                .WithTitle($"{verse.BookName} {verse.Chapter}:{verse.Verse}")
                                .WithDescription(verse.Text))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Quran(string ayah)
        {
            var res = await _service.GetQuranVerseAsync(ayah);

            if (!res.TryPickT0(out var qr, out var error))
            {
                await Response().Error(error.Value).SendAsync();
                return;
            }

            var english = qr.Data[0];
            var arabic = qr.Data[1];

            using var http = _httpFactory.CreateClient();
            await using var audio = await http.GetStreamAsync(arabic.Audio);

            await Response()
                  .Embed(CreateEmbed()
                                .WithOkColor()
                                .AddField("Arabic", arabic.Text)
                                .AddField("English", english.Text)
                                .WithFooter(arabic.Number.ToString()))
                  .File(audio, Uri.EscapeDataString(ayah) + ".mp3")
                  .SendAsync();
        }
    }
}