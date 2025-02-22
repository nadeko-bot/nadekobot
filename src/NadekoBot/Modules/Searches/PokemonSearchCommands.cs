#nullable disable
using NadekoBot.Modules.Searches.Services;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class PokemonSearchCommands : NadekoModule<SearchesService>
    {
        private readonly ILocalDataCache _cache;

        public PokemonSearchCommands(ILocalDataCache cache)
            => _cache = cache;

        [Cmd]
        public async Task Pokemon([Leftover] string pokemon = null)
        {
            pokemon = pokemon?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(pokemon))
                return;

            foreach (var kvp in await _cache.GetPokemonsAsync())
            {
                if (kvp.Key.ToUpperInvariant() == pokemon.ToUpperInvariant())
                {
                    var p = kvp.Value;
                    await Response().Embed(CreateEmbed()
                                                    .WithOkColor()
                                                    .WithTitle(kvp.Key.ToTitleCase())
                                                    .WithDescription(p.BaseStats.ToString())
                                                    .WithThumbnailUrl(
                                                        $"https://assets.pokemon.com/assets/cms2/img/pokedex/detail/{p.Id.ToString("000")}.png")
                                                    .AddField(GetText(strs.types), string.Join("\n", p.Types), true)
                                                    .AddField(GetText(strs.height_weight),
                                                        GetText(strs.height_weight_val(p.HeightM, p.WeightKg)),
                                                        true)
                                                    .AddField(GetText(strs.abilities),
                                                        string.Join("\n", p.Abilities.Select(a => a.Value)),
                                                        true)).SendAsync();
                    return;
                }
            }

            await Response().Error(strs.pokemon_none).SendAsync();
        }

        [Cmd]
        public async Task PokemonAbility([Leftover] string ability = null)
        {
            ability = ability?.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.InvariantCulture);
            if (string.IsNullOrWhiteSpace(ability))
                return;
            foreach (var kvp in await _cache.GetPokemonAbilitiesAsync())
            {
                if (kvp.Key.ToUpperInvariant() == ability)
                {
                    await Response().Embed(CreateEmbed()
                                                    .WithOkColor()
                                                    .WithTitle(kvp.Value.Name)
                                                    .WithDescription(string.IsNullOrWhiteSpace(kvp.Value.Desc)
                                                        ? kvp.Value.ShortDesc
                                                        : kvp.Value.Desc)
                                                    .AddField(GetText(strs.rating),
                                                        kvp.Value.Rating.ToString(Culture),
                                                        true)).SendAsync();
                    return;
                }
            }

            await Response().Error(strs.pokemon_ability_none).SendAsync();
        }
    }
}