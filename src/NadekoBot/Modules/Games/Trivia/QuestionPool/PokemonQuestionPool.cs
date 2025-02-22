namespace NadekoBot.Modules.Games.Common.Trivia;

public sealed class PokemonQuestionPool : IQuestionPool
{
    public int QuestionsCount => 905; // xd
    private readonly NadekoRandom _rng;
    private readonly ILocalDataCache _cache;

    public PokemonQuestionPool(ILocalDataCache cache)
    {
        _cache = cache;
        _rng = new NadekoRandom();
    }

    public async Task<TriviaQuestion?> GetQuestionAsync()
    {
        var pokes = await _cache.GetPokemonMapAsync();

        if (pokes is null or { Count: 0 })
            return default;
            
        var num = _rng.Next(1, QuestionsCount + 1);
        return new(new()
        {
            Question = "Who's That Pokémon?",
            Answer = pokes[num].ToTitleCase(),
            Category = "Pokemon",
            ImageUrl = $@"https://nadeko.bot/images/pokemon/shadows/{num}.png",
            AnswerImageUrl = $@"https://nadeko.bot/images/pokemon/real/{num}.png"
        });
    }
}