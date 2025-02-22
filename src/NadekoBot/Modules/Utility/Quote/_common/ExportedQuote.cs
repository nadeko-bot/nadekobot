using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public class ExportedQuote
{
    public required string Id { get; init; }
    public required string An { get; init; }
    public required ulong Aid { get; init; }
    public required string Txt { get; init; }

    public static ExportedQuote FromModel(Quote quote)
        => new()
        {
            Id = ((kwum)quote.Id).ToString(),
            An = quote.AuthorName,
            Aid = quote.AuthorId,
            Txt = quote.Text
        };
}