using OneOf;
using OneOf.Types;
using System.Net;
using System.Net.Http.Json;

namespace NadekoBot.Modules.Searches;

public sealed class ReligiousApiService : INService
{
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

            if (res is null || res.Error is not null || res.Verses is null || res.Verses.Length == 0)
            {
                return new Error<string>(res?.Error ?? "No verse found.");
            }

            return res.Verses[0];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new Error<string>("No verse found.");
        }
    }

    public async Task<OneOf<QuranResponse<QuranAyah>, Error<LocStr>>> GetQuranVerseAsync(string ayah)
    {
        if (string.IsNullOrWhiteSpace(ayah))
            return new Error<LocStr>(strs.invalid_input);

        ayah = Uri.EscapeDataString(ayah);

        using var http = _httpFactory.CreateClient();
        var res = await http.GetFromJsonAsync<QuranResponse<QuranAyah>>(
            $"https://api.alquran.cloud/v1/ayah/{ayah}/editions/en.asad,ar.alafasy");

        if (res is null or not { Code: 200 })
        {
            return new Error<LocStr>(strs.not_found);
        }

        return res;
    }
}