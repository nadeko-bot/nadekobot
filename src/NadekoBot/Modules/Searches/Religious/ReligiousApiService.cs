using OneOf;
using OneOf.Types;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NadekoBot.Modules.Searches;

public sealed class ReligiousApiService : INService
{
    private const string NO_VERSE = "No verse found.";

    private readonly IHttpClientFactory _httpFactory;

    public ReligiousApiService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public async Task<OneOf<BibleVerse, Error<string>>> GetBibleVerseAsync(string book, string chapterAndVerse)
    {
        if (string.IsNullOrWhiteSpace(book) || string.IsNullOrWhiteSpace(chapterAndVerse))
            return new Error<string>("Invalid input.");


        book = Uri.EscapeDataString(book);
        chapterAndVerse = Uri.EscapeDataString(chapterAndVerse);

        using var http = _httpFactory.CreateClient();
        try
        {
            var res = await http.GetFromJsonAsync<BibleVerses>($"https://bible-api.com/{book} {chapterAndVerse}");

            if (res is null || res.Error is not null || res.Verses is not [{ Text: { Length: > 0 } } verse, ..])
                return new Error<string>(res?.Error ?? NO_VERSE);

            return verse;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new Error<string>(NO_VERSE);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("bible-api.com", ex);
            return new Error<string>(NO_VERSE);
        }
    }

    public async Task<OneOf<QuranVerse, Error<LocStr>>> GetQuranVerseAsync(string ayah)
    {
        if (string.IsNullOrWhiteSpace(ayah))
            return new Error<LocStr>(strs.invalid_input);

        ayah = Uri.EscapeDataString(ayah);

        using var http = _httpFactory.CreateClient();
        QuranResponse<QuranAyah>? res;
        try
        {
            res = await http.GetFromJsonAsync<QuranResponse<QuranAyah>>(
                $"https://api.alquran.cloud/v1/ayah/{ayah}/editions/en.asad,ar.alafasy");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new Error<LocStr>(strs.not_found);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("alquran.cloud", ex);
            return new Error<LocStr>(strs.not_found);
        }

        // The site sends the editions in the order of the url: English, then Arabic.
        if (res is not { Code: 200, Data: [{ Text: { Length: > 0 } english }, { Text: { Length: > 0 } arabic } arabicAyah, ..] })
            return new Error<LocStr>(strs.not_found);

        return new QuranVerse(arabicAyah.Number, arabic, english, arabicAyah.Audio);
    }
}
