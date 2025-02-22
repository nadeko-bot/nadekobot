namespace NadekoBot.Modules.Games.Common.Trivia;

public sealed class DefaultQuestionPool : IQuestionPool
{
    private readonly ILocalDataCache _cache;
    private readonly NadekoRandom _rng;

    public DefaultQuestionPool(ILocalDataCache cache)
    {
        _cache = cache;
        _rng = new NadekoRandom();
    }
    public async Task<TriviaQuestion?> GetQuestionAsync()
    {
        var pool = await _cache.GetTriviaQuestionsAsync();
        
        if(pool is null or {Length: 0})
            return default;
        
        return new(pool[_rng.Next(0, pool.Length)]);
    }
}