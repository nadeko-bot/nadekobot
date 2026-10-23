using NadekoBot.Modules.Searches.Services;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class MovieCommands : NadekoModule<MovieService>
    {
        private async Task<bool> ValidateQuery([MaybeNullWhen(false)] string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
                return true;

            await Response().Error(strs.specify_search_params).SendAsync();
            return false;
        }
        
        [Cmd]
        public async Task Movie([Leftover] string query)
        {
            if (!await ValidateQuery(query))
                return;

            await ctx.Channel.TriggerTypingAsync();

            var result = await _service.GetMovieDataAsync(query);
            if (result.TryPickT1(out var error, out var movie))
            {
                await Response()
                      .Error(error switch
                      {
                          MovieError.MissingKey => strs.omdb_api_missing,
                          MovieError.KeyRefused => strs.omdb_api_refused,
                          _ => strs.imdb_fail
                      })
                      .SendAsync();
                return;
            }

            // Rating, Genre, and Year are used directly as strings here, not resource keys
            await Response()
                  .Embed(CreateEmbed()
                         .WithOkColor()
                         .WithTitle(movie.Title)
                         .WithUrl($"https://www.imdb.com/title/{movie.ImdbId}/")
                         .WithDescription(movie.Plot.TrimTo(1000))
                         .AddField("Rating", movie.ImdbRating ?? "N/A", true)
                         .AddField("Genre", movie.Genre ?? "N/A", true)
                         .AddField("Year", movie.Year ?? "N/A", true)
                         .WithImageUrl(Uri.IsWellFormedUriString(movie.Poster, UriKind.Absolute)
                             ? movie.Poster
                             : null))
                  .SendAsync();
        }
    }
}
