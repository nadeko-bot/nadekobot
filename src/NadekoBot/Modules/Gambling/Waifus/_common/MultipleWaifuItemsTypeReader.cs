#nullable disable
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Services;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Gambling;

public partial class MultipleWaifuItemsTypeReader : NadekoTypeReader<MultipleWaifuItems>
{
    private readonly WaifuService _service;
    
    [GeneratedRegex(@"(?:(?<count>\d+)[x*])?(?<item>.+)")]
    private static partial Regex ItemRegex();

    public MultipleWaifuItemsTypeReader(WaifuService service)
    {
        _service = service;
    }
    public override ValueTask<TypeReaderResult<MultipleWaifuItems>> ReadAsync(ICommandContext ctx, string input)
    {
        input = input.ToLowerInvariant();
        var match = ItemRegex().Match(input);
        if (!match.Success)
        {
            return new(Discord.Commands.TypeReaderResult.FromError(CommandError.ParseFailed, "Invalid input."));
        }

        var count = 1;
        if (match.Groups["count"].Success)
        {
            if (!int.TryParse(match.Groups["count"].Value, out count) || count < 1)
            {
                return new(Discord.Commands.TypeReaderResult.FromError(CommandError.ParseFailed, "Invalid count."));
            }
        }
        
        var itemName = match.Groups["item"].Value?.ToLowerInvariant();
        var allItems = _service.GetWaifuItems();
        var item = allItems.FirstOrDefault(x => x.Name.ToLowerInvariant() == itemName);
        if (item is null)
        {
            return new(Discord.Commands.TypeReaderResult.FromError(CommandError.ParseFailed, "Waifu gift does not exist."));
        }
        
        return new(Discord.Commands.TypeReaderResult.FromSuccess(new MultipleWaifuItems(count, item)));
    }
}