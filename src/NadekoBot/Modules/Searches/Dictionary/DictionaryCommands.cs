using NadekoBot.Modules.Searches.Common;
using NadekoBot.Modules.Searches.Services;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class DictionaryCommands : NadekoModule<SearchesService>
    {
        private readonly IHttpClientFactory _httpFactory;

        public DictionaryCommands(IHttpClientFactory factory)
        {
            _httpFactory = factory;
        }
        
        private async Task<bool> ValidateQuery([MaybeNullWhen(false)] string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
                return true;

            await Response().Error(strs.specify_search_params).SendAsync();
            return false;
        }
        
        public Task<IUserMessage> HandleErrorAsync(ErrorType error)
        {
            var errorKey = error switch
            {
                ErrorType.ApiKeyMissing => strs.api_key_missing,
                ErrorType.InvalidInput => strs.invalid_input,
                ErrorType.NotFound => strs.not_found,
                ErrorType.Unknown => strs.error_occured,
                _ => strs.error_occured,
            };

            return Response().Error(errorKey).SendAsync();
        }
        
        [Cmd]
        public async Task UrbanDict([Leftover] string query)
        {
            if (!await ValidateQuery(query))
                return;

            await ctx.Channel.TriggerTypingAsync();
            using var http = _httpFactory.CreateClient();
            var res = await http.GetStringAsync($"https://api.urbandictionary.com/v0/define?"
                                             + $"term={Uri.EscapeDataString(query ?? "")}");
            var allItems = JsonConvert.DeserializeObject<UrbanResponse>(res)?.List;

            if (allItems is null or { Length: 0 })
            {
                await Response().Error(strs.ud_error).SendAsync();
                return;
            }

            await Response()
                  .Paginated()
                  .Items(allItems)
                  .PageSize(1)
                  .CurrentPage(0)
                  .Page((items, _) =>
                  {
                      var item = items[0];
                      return CreateEmbed()
                             .WithOkColor()
                             .WithUrl(item.Permalink)
                             .WithTitle(item.Word)
                             .WithDescription(item.Definition);
                  })
                  .SendAsync();
        }

        [Cmd]
        public async Task Define([Leftover] string word)
        {
            if (!await ValidateQuery(word))
                return;


            var maybeItems = await _service.GetDefinitionsAsync(word);

            if (!maybeItems.TryPickT0(out var defs, out var error))
            {
                await HandleErrorAsync(error);
                return;
            }

            await Response()
                  .Paginated()
                  .Items(defs)
                  .PageSize(1)
                  .Page((items, _) =>
                  {
                      var model = items.First();
                      var embed = CreateEmbed()
                                  .WithDescription(ctx.User.Mention)
                                  .AddField(GetText(strs.word), model.Word, true)
                                  .AddField(GetText(strs._class), model.WordType, true)
                                  .AddField(GetText(strs.definition), model.Definition)
                                  .WithOkColor();

                      if (!string.IsNullOrWhiteSpace(model.Example))
                          embed.AddField(GetText(strs.example), model.Example);

                      return embed;
                  })
                  .SendAsync();
        }
    }
}
