using NadekoBot.Common.Pokemon;
using NadekoBot.Modules.Games.Common.Trivia;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Services;

public sealed class LocalDataCache : ILocalDataCache, INService
{
    private const string POKEMON_ABILITIES_FILE = "data/pokemon/pokemon_abilities.json";
    private const string POKEMON_LIST_FILE = "data/pokemon/pokemon_list.json";
    private const string POKEMON_MAP_PATH = "data/pokemon/name-id_map.json";
    private const string QUESTIONS_FILE = "data/trivia_questions.json";

    private readonly IBotCache _cache;

    private readonly JsonSerializerOptions _opts = new JsonSerializerOptions()
    {
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true
    };

    public LocalDataCache(IBotCache cache)
        => _cache = cache;

    private async Task<T?> GetOrCreateCachedDataAsync<T>(
        TypedKey<T> key,
        string fileName)
        => await _cache.GetOrAddAsync(key,
            async () =>
            {
                if (!File.Exists(fileName))
                {
                    Log.Warning($"{fileName} is missing. Relevant data can't be loaded");
                    return default;
                }

                try
                {
                    await using var stream = File.OpenRead(fileName);
                    return await JsonSerializer.DeserializeAsync<T>(stream, _opts);
                }
                catch (Exception ex)
                {
                    Log.Error(ex,
                        "Error reading {FileName} file: {ErrorMessage}",
                        fileName,
                        ex.Message);

                    return default;
                }
            });


    private static TypedKey<IReadOnlyDictionary<string, SearchPokemon>> _pokemonListKey
        = new("pokemon:list");

    public async Task<IReadOnlyDictionary<string, SearchPokemon>?> GetPokemonsAsync()
        => await GetOrCreateCachedDataAsync(_pokemonListKey, POKEMON_LIST_FILE);


    private static TypedKey<IReadOnlyDictionary<string, SearchPokemonAbility>> _pokemonAbilitiesKey
        = new("pokemon:abilities");

    public async Task<IReadOnlyDictionary<string, SearchPokemonAbility>?> GetPokemonAbilitiesAsync()
        => await GetOrCreateCachedDataAsync(_pokemonAbilitiesKey, POKEMON_ABILITIES_FILE);


    private static TypedKey<IReadOnlyDictionary<int, string>> _pokeMapKey
        = new("pokemon:ab_map2"); // 2 because ab_map was storing arrays

    public async Task<IReadOnlyDictionary<int, string>?> GetPokemonMapAsync()
        => await _cache.GetOrAddAsync(_pokeMapKey,
            async () =>
            {
                var fileName = POKEMON_MAP_PATH;
                if (!File.Exists(fileName))
                {
                    Log.Warning($"{fileName} is missing. Relevant data can't be loaded");
                    return default;
                }

                try
                {
                    await using var stream = File.OpenRead(fileName);
                    var arr = await JsonSerializer.DeserializeAsync<PokemonNameId[]>(stream, _opts);

                    return (IReadOnlyDictionary<int, string>?)arr?.ToDictionary(x => x.Id, x => x.Name);
                }
                catch (Exception ex)
                {
                    Log.Error(ex,
                        "Error reading {FileName} file: {ErrorMessage}",
                        fileName,
                        ex.Message);

                    return default;
                }
            });


    private static TypedKey<TriviaQuestionModel[]> _triviaKey
        = new("trivia:questions");

    public async Task<TriviaQuestionModel[]?> GetTriviaQuestionsAsync()
        => await GetOrCreateCachedDataAsync(_triviaKey, QUESTIONS_FILE);
}