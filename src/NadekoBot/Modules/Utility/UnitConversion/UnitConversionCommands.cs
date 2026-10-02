using System.Text;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Modules.Utility.UnitConversion;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class UnitConverterCommands : NadekoModule<ConverterService>
    {
        private const int CURRENCIES_PER_PAGE = 40;
        private const string EXAMPLE_ARGS = "5 km mi";

        private static readonly Lazy<ListPage[]> _unitPages = new(BuildUnitPages);

        private readonly record struct ListPage(string Title, string Body, bool IsUnits);

        private readonly record struct ConvertReply(LocStr? Error, string? Text, EmbedBuilder? Embed);

        [Cmd]
        public async Task Convert([Leftover] string input)
        {
            var reply = BuildReply(input ?? string.Empty);

            if (reply.Error is { } err)
                await Response().Error(err).SendAsync();
            else if (reply.Embed is not null)
                await Response().Embed(reply.Embed).SendAsync();
            else
                await Response().Confirm(reply.Text!).SendAsync();
        }

        [Cmd]
        public async Task ConvertList(int page = 1)
        {
            if (--page < 0)
                return;

            var unitPages = _unitPages.Value;
            var codes = _service.Rates.Codes;
            var currencyPageCount = (codes.Length + CURRENCIES_PER_PAGE - 1) / CURRENCIES_PER_PAGE;
            var pages = new ListPage[unitPages.Length + currencyPageCount];
            unitPages.CopyTo(pages, 0);
            for (var i = 0; i < currencyPageCount; i++)
                pages[unitPages.Length + i] = BuildCurrencyPage(codes, i);

            var prefixNote = GetText(strs.convertlist_prefixes);
            await Response()
                  .Paginated()
                  .Items(pages)
                  .PageSize(1)
                  .CurrentPage(page)
                  .Page((items, _) =>
                  {
                      var p = items[0];
                      return CreateEmbed()
                             .WithOkColor()
                             .WithTitle(GetText(strs.convertlist_title(p.Title)))
                             .WithDescription(p.IsUnits ? $"{p.Body}\n{prefixNote}" : p.Body);
                  })
                  .SendAsync();
        }

        private ConvertReply BuildReply(string input)
        {
            if (!ConvertInputParser.TryParse(input, Culture, out var parsed))
                return new(strs.convert_invalid_input(Format.Code(prefix + "convert " + EXAMPLE_ARGS)), null, null);

            if (!parsed.HasTarget)
            {
                var common = _service.ConvertToCommon(parsed.From, parsed.Value);
                if (common.Status != ConvertStatus.Ok)
                {
                    return new(GetError(common.Status, common.FailedToken, common.Suggestion, parsed.From, default),
                        null,
                        null);
                }

                var sb = new StringBuilder();
                foreach (var t in common.Targets)
                {
                    sb.Append("= ")
                      .AppendLine(Format.Bold(ConvertFormatter.FormatQuantity(t.Value, t.Unit, t.Symbol, Culture)));
                }

                var source = ConvertFormatter.FormatQuantity(parsed.Value, common.From, parsed.From, Culture);
                var embed = CreateEmbed()
                            .WithOkColor()
                            .WithTitle(source)
                            .WithDescription(sb.ToString());
                return new(null, null, embed);
            }

            var res = _service.Convert(parsed.From, parsed.To, parsed.Value);
            if (res.Status != ConvertStatus.Ok)
                return new(GetError(res.Status, res.FailedToken, res.Suggestion, parsed.From, parsed.To), null, null);

            var from = ConvertFormatter.FormatQuantity(parsed.Value, res.From, parsed.From, Culture);
            var to = ConvertFormatter.FormatQuantity(res.Value, res.To, parsed.To, Culture);
            return new(null, GetText(strs.convert_result(Format.Bold(from), Format.Bold(to))), null);
        }

        private static LocStr GetError(
            ConvertStatus status,
            string? failed,
            string? suggestion,
            ReadOnlySpan<char> from,
            ReadOnlySpan<char> to)
            => status switch
            {
                ConvertStatus.UnknownUnit when suggestion is not null
                    => strs.convert_unknown_unit_suggest(Format.Bold(failed ?? string.Empty), Format.Bold(suggestion)),
                ConvertStatus.UnknownUnit => strs.convert_unknown_unit(Format.Bold(failed ?? string.Empty)),
                ConvertStatus.InvalidExpression => strs.convert_invalid_unit(Format.Bold(failed ?? string.Empty)),
                ConvertStatus.DimensionMismatch
                    => strs.convert_type_error(Format.Bold(from.ToString()), Format.Bold(to.ToString())),
                ConvertStatus.RatesUnavailable => strs.convert_rates_unavailable,
                ConvertStatus.NoCommonTargets => strs.convert_no_targets(Format.Bold(from.ToString())),
                _ => strs.convert_overflow
            };

        private static ListPage[] BuildUnitPages()
        {
            var categories = Enum.GetValues<UnitCategory>();
            var pages = new List<ListPage>(categories.Length);
            var sb = new StringBuilder();
            foreach (var cat in categories)
            {
                sb.Clear();
                foreach (ref readonly var u in UnitCatalog.Units.AsSpan())
                {
                    if (u.Category != cat)
                        continue;

                    sb.Append('`').Append(u.Symbol).Append('`');
                    if (u.Prefixes != PrefixSet.None)
                        sb.Append('*');

                    sb.Append(' ').AppendLine(u.Name);
                }

                if (sb.Length > 0)
                    pages.Add(new(UnitCatalog.GetCategoryName(cat), sb.ToString(), true));
            }

            return [.. pages];
        }

        private static ListPage BuildCurrencyPage(string[] codes, int pageIndex)
        {
            var sb = new StringBuilder();
            var start = pageIndex * CURRENCIES_PER_PAGE;
            var end = Math.Min(start + CURRENCIES_PER_PAGE, codes.Length);
            for (var i = start; i < end; i++)
            {
                sb.Append('`').Append(codes[i]).Append('`');
                if (CurrencyRates.GetName(codes[i]) is { } name)
                    sb.Append(' ').Append(name);

                sb.AppendLine();
            }

            return new(UnitCatalog.GetCategoryName(UnitCategory.Currency), sb.ToString(), false);
        }
    }
}
