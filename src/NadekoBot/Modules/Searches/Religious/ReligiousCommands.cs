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

            if (!res.TryPickT0(out var verse, out var error))
            {
                await Response().Error(error.Value).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                        .WithOkColor()
                        .AddField("Arabic", verse.Arabic)
                        .AddField("English", verse.English);

            if (verse.Number is { } number)
                embed.WithFooter(number.ToString());

            if (verse.AudioUrl is null)
            {
                await Response().Embed(embed).SendAsync();
                return;
            }

            using var http = _httpFactory.CreateClient(ExternalHttp.CLIENT_NAME);
            Stream audio;
            try
            {
                audio = await http.GetStreamAsync(verse.AudioUrl);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Log.Warning("Unable to download the audio of ayah {Ayah}: {ErrorMessage}", ayah, ex.Message);
                await Response().Embed(embed).SendAsync();
                return;
            }

            await using (audio)
            {
                await Response()
                      .Embed(embed)
                      .File(audio, Uri.EscapeDataString(ayah) + ".mp3")
                      .SendAsync();
            }
        }
    }
}