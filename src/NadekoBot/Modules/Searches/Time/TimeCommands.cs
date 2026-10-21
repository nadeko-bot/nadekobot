using NadekoBot.Modules.Searches.Services;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class TimeCommands : NadekoModule<SearchesService>
    {
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
                ErrorType.ApiKeyInactive => strs.timezone_db_api_key,
                ErrorType.Unknown => strs.error_occured,
                _ => strs.error_occured,
            };

            return Response().Error(errorKey).SendAsync();
        }

        [Cmd]
        public async Task Time([Leftover] string query)
        {
            if (!await ValidateQuery(query))
                return;

            await ctx.Channel.TriggerTypingAsync();

            var (data, err) = await _service.GetTimeDataAsync(query);
            if (err is not null)
            {
                await HandleErrorAsync(err.Value);
                return;
            }

            var eb = CreateEmbed()
                     .WithOkColor()
                     .WithTitle(GetText(strs.time_new))
                     .WithDescription(Format.Code(data.Time.DateTime.ToString(Culture)))
                     .AddField(GetText(strs.location), string.Join('\n', data.Address.Split(", ")), true)
                     .AddField(GetText(strs.timezone), $"{data.ZoneId} (UTC{data.Time:zzz})", true);

            await Response().Embed(eb).SendAsync();
        }
    }
}
