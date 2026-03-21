using NadekoBot.Modules.Searches.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class UtilityCommands : NadekoModule<SearchesService>
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
                ErrorType.Unknown => strs.error_occured,
                _ => strs.error_occured,
            };

            return Response().Error(errorKey).SendAsync();
        }
        
        [Cmd]
        public async Task Lmgtfy([Leftover] string smh)
        {
            if (!await ValidateQuery(smh))
                return;

            var link = $"https://letmegooglethat.com/?q={Uri.EscapeDataString(smh ?? "")}";
            var shortenedUrl = await _service.ShortenLink(link) ?? link;
            await Response().Confirm($"<{shortenedUrl}>").SendAsync();
        }

        [Cmd]
        public async Task Shorten([Leftover] string query)
        {
            if (!await ValidateQuery(query))
                return;

            var shortLink = await _service.ShortenLink(query);

            if (shortLink is null)
            {
                await Response().Error(strs.error_occured).SendAsync();
                return;
            }

            await Response()
                  .Embed(CreateEmbed()
                         .WithOkColor()
                         .AddField(GetText(strs.original_url), $"<{query}>")
                         .AddField(GetText(strs.short_url), $"<{shortLink}>"))
                  .SendAsync();
        }
        
        [Cmd]
        public async Task Color(params Rgba32[] colors)
        {
            if (!colors.Any())
                return;

            var colorObjects = colors.Take(10).ToArray();

            using var img = new Image<Rgba32>(colorObjects.Length * 50, 50);
            for (var i = 0; i < colorObjects.Length; i++)
            {
                var x = i * 50;
                var j = i;
                img.Mutate(m => m.FillPolygon(colorObjects[j], new(x, 0), new(x + 50, 0), new(x + 50, 50), new(x, 50)));
            }

            await using var ms = await img.ToStreamAsync();
            await ctx.Channel.SendFileAsync(ms, "colors.png");
        }
    }
}
