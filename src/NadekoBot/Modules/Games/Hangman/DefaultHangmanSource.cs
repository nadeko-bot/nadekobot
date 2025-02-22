using NadekoBot.Common.Yml;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Games.Hangman;

public sealed class DefaultHangmanSource : IHangmanSource
{
    private IReadOnlyDictionary<string, HangmanTerm[]> termsDict = new Dictionary<string, HangmanTerm[]>();
    private readonly Random _rng;

    public DefaultHangmanSource()
    {
        _rng = new NadekoRandom();
        Reload();
    }

    public void Reload()
    {
        if (!Directory.Exists("data/hangman"))
        {
            Log.Error("Hangman game won't work. Folder 'data/hangman' is missing");
            return;
        }

        var qs = new Dictionary<string, HangmanTerm[]>();
        foreach (var file in Directory.EnumerateFiles("data/hangman/", "*.yml"))
        {
            try
            {
                var data = Yaml.Deserializer.Deserialize<HangmanTerm[]>(File.ReadAllText(file));
                qs[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = data;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Loading {HangmanFile} failed", file);
            }
        }

        termsDict = qs;

        Log.Information("Loaded {HangmanCategoryCount} hangman categories", qs.Count);
    }

    public IReadOnlyCollection<string> GetCategories()
        => termsDict.Keys.ToList();

    public bool GetTerm(string? category, [NotNullWhen(true)] out HangmanTerm? term)
    {
        if (category is null)
        {
            var cats = GetCategories();
            category = cats.ElementAt(_rng.Next(0, cats.Count));
        }

        if (termsDict.TryGetValue(category, out var terms))
        {
            term = terms[_rng.Next(0, terms.Length)];
            return true;
        }

        term = null;
        return false;
    }
}