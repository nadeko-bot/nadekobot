#nullable disable
using NadekoBot.Common.Pokemon;
using NadekoBot.Modules.Games.Common.Trivia;

namespace NadekoBot.Services;

public interface ILocalDataCache
{
    Task<IReadOnlyDictionary<string, SearchPokemon>> GetPokemonsAsync();
    Task<IReadOnlyDictionary<string, SearchPokemonAbility>> GetPokemonAbilitiesAsync();
    Task<TriviaQuestionModel[]> GetTriviaQuestionsAsync();
    Task<IReadOnlyDictionary<int, string>> GetPokemonMapAsync();
}