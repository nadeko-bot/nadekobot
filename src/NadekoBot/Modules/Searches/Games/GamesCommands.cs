using NadekoBot.Modules.Searches.Services;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class GamesCommands(IBotCreds creds) : NadekoModule<SearchesService>
    {
        private async Task<bool> ValidateQuery([MaybeNullWhen(false)] string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
                return true;

            await Response().Error(strs.specify_search_params).SendAsync();
            return false;
        }
        
        [Cmd]
        public async Task MagicTheGathering([Leftover] string search)
        {
            if (!await ValidateQuery(search))
                return;

            await ctx.Channel.TriggerTypingAsync();
            var card = await _service.GetMtgCardAsync(search);

            if (card is null)
            {
                await Response().Error(strs.card_not_found).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                        .WithOkColor()
                        .WithTitle(card.Name)
                        .WithDescription(card.Description)
                        .WithImageUrl(card.ImageUrl)
                        .AddField(GetText(strs.store_url), card.StoreUrl, true)
                        .AddField(GetText(strs.cost), string.IsNullOrWhiteSpace(card.ManaCost) ? "-" : card.ManaCost, true)
                        .AddField(GetText(strs.types), string.IsNullOrWhiteSpace(card.Types) ? "-" : card.Types, true);

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        public async Task Hearthstone([Leftover] string name)
        {
            if (!await ValidateQuery(name))
                return;

            if (string.IsNullOrWhiteSpace(creds.RapidApiKey))
            {
                await Response().Error(strs.mashape_api_missing).SendAsync();
                return;
            }

            await ctx.Channel.TriggerTypingAsync();
            var card = await _service.GetHearthstoneCardDataAsync(name);

            if (card is null)
            {
                await Response().Error(strs.card_not_found).SendAsync();
                return;
            }

            var embed = CreateEmbed().WithOkColor().WithImageUrl(card.Img);

            if (!string.IsNullOrWhiteSpace(card.Flavor))
                embed.WithDescription(card.Flavor);

            await Response().Embed(embed).SendAsync();
        }
        
        [Cmd]
        public async Task Steam([Leftover] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return;

            if (string.IsNullOrWhiteSpace(creds.SteamApiKey))
            {
                await Response().Error(strs.steam_api_missing).SendAsync();
                return;
            }

            await ctx.Channel.TriggerTypingAsync();

            var appId = await _service.GetSteamAppIdByName(query);
            if (appId == -1)
            {
                await Response().Error(strs.not_found).SendAsync();
                return;
            }

            await Response().Text($"https://store.steampowered.com/app/{appId}").SendAsync();
        }
    }
}
