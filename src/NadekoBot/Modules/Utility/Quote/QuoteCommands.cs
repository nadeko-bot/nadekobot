#nullable disable warnings
using NadekoBot.Common.Yml;
using NadekoBot.Db.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class QuoteCommands : NadekoModule
    {
        private const string PREPEND_EXPORT =
            """
            # Keys are keywords, Each key has a LIST of quotes in the following format:
            # - id: Alphanumeric id used for commands related to the quote. (Note, when using .quotesimport, a new id will be generated.)
            #   an: Author name
            #   aid: Author id
            #   txt: Quote text

            """;

        private static readonly ISerializer _exportSerializer = new SerializerBuilder()
                                                                .WithEventEmitter(args
                                                                    => new MultilineScalarFlowStyleEmitter(args))
                                                                .WithNamingConvention(
                                                                    CamelCaseNamingConvention.Instance)
                                                                .WithIndentedSequences()
                                                                .ConfigureDefaultValuesHandling(DefaultValuesHandling
                                                                    .OmitDefaults)
                                                                .DisableAliases()
                                                                .Build();

        private readonly DbService _db;
        private readonly IHttpClientFactory _http;
        private readonly IQuoteService _qs;

        public QuoteCommands(DbService db, IQuoteService qs, IHttpClientFactory http)
        {
            _db = db;
            _http = http;
            _qs = qs;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public Task QuoteList(OrderType order = OrderType.Keyword)
            => QuoteList(1, order);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public async Task QuoteList(int page = 1, OrderType order = OrderType.Keyword)
        {
            page -= 1;
            if (page < 0)
                return;

            var quotes = await _qs.GetAllQuotesAsync(ctx.Guild.Id, page, order);

            if (quotes.Count == 0)
            {
                await Response().Error(strs.quotes_page_none).SendAsync();
                return;
            }

            var list = quotes.Select(q => $"`{new kwum(q.Id)}` {Format.Bold(q.Keyword),-20} by {q.AuthorName}")
                             .Join("\n");

            await Response()
                  .Confirm(GetText(strs.quotes_page(page + 1)), list)
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuotePrint([Leftover] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return;

            keyword = keyword.ToUpperInvariant();

            var quote = await _qs.GetQuoteByKeywordAsync(ctx.Guild.Id, keyword);

            if (quote is null)
                return;

            var repCtx = new ReplacementContext(Context);

            var text = SmartText.CreateFrom(quote.Text);
            text = await repSvc.ReplaceAsync(text, repCtx);

            await Response()
                  .Text($"`{new kwum(quote.Id)}` 📣 " + text)
                  .SendAsync();
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuoteShow(kwum quoteId)
        {
            var quote = await _qs.GetQuoteByIdAsync(ctx.Guild.Id, quoteId);

            if (quote is null)
            {
                await Response().Error(strs.quotes_notfound).SendAsync();
                return;
            }

            await ShowQuoteData(quote);
        }

        private NadekoInteractionBase CreateEditInteraction(kwum id, Quote found)
        {
            var modal = new ModalBuilder()
                        .WithCustomId("quote:edit_modal")
                        .WithTitle($"Edit expression {id}")
                        .AddTextInput(new TextInputBuilder()
                                      .WithLabel(GetText(strs.response))
                                      .WithValue(found.Text)
                                      .WithMinLength(1)
                                      .WithCustomId("quote:edit_modal:response")
                                      .WithStyle(TextInputStyle.Paragraph));

            var inter = _inter.Create(ctx.User.Id,
                new ButtonBuilder()
                    .WithEmote(Emoji.Parse("📝"))
                    .WithLabel("Edit")
                    .WithStyle(ButtonStyle.Primary)
                    .WithCustomId("test"),
                modal,
                async (sm) =>
                {
                    var msg = sm.Data.Components.FirstOrDefault()?.Value;

                    if (!string.IsNullOrWhiteSpace(msg))
                        await QuoteEdit(id, msg);
                }
            );
            return inter;
        }

        private async Task ShowQuoteData(Quote quote)
        {
            var inter = CreateEditInteraction(quote.Id, quote);
            var eb = CreateEmbed()
                            .WithOkColor()
                            .WithTitle($"{GetText(strs.quote_id($"`{new kwum(quote.Id)}"))}`")
                            .WithDescription(Format.Sanitize(quote.Text).Replace("](", "]\\(").TrimTo(4096))
                            .AddField(GetText(strs.trigger), quote.Keyword)
                            .WithFooter(
                                GetText(strs.created_by($"{quote.AuthorName} ({quote.AuthorId})")));

            if (!(quote.Text.Length > 4096))
            {
                await Response().Embed(eb).Interaction(quote.AuthorId == ctx.User.Id ? inter : null).SendAsync();
                return;
            }

            await using var textStream = await quote.Text.ToStream();

            await Response()
                  .Embed(eb)
                  .File(textStream, "quote.txt")
                  .SendAsync();
        }

        private async Task QuoteSearchInternalAsync(string? keyword, string textOrAuthor)
        {
            if (string.IsNullOrWhiteSpace(textOrAuthor))
                return;

            keyword = keyword?.ToUpperInvariant();

            var quotes = await _qs.SearchQuoteKeywordTextAsync(ctx.Guild.Id, keyword, textOrAuthor);

            await Response()
                  .Paginated()
                  .Items(quotes)
                  .PageSize(1)
                  .Page((pageQuotes, _) =>
                  {
                      var quote = pageQuotes[0];

                      var text = quote.Keyword.ToLowerInvariant() + ":  " + quote.Text;

                      return CreateEmbed()
                                    .WithOkColor()
                                    .WithTitle($"{new kwum(quote.Id)} 💬 ")
                                    .WithDescription(text);
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        public Task QuoteSearch(string textOrAuthor)
            => QuoteSearchInternalAsync(null, textOrAuthor);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public Task QuoteSearch(string keyword, [Leftover] string textOrAuthor)
            => QuoteSearchInternalAsync(keyword, textOrAuthor);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuoteId(kwum quoteId)
        {
            if (quoteId < 0)
                return;

            var quote = await _qs.GetQuoteByIdAsync(ctx.Guild.Id, quoteId);

            if (quote is null)
            {
                await Response().Error(strs.quotes_notfound).SendAsync();
                return;
            }

            var infoText = $"*`{new kwum(quote.Id)}` added by {quote.AuthorName}* 🗯️ "
                           + quote.Keyword.ToLowerInvariant()
                           + ":\n";


            var repCtx = new ReplacementContext(Context);
            var text = SmartText.CreateFrom(quote.Text);
            text = await repSvc.ReplaceAsync(text, repCtx);
            await Response()
                  .Text(infoText + text)
                  .SendAsync();
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuoteAdd(string keyword, [Leftover] string text)
        {
            if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(text))
                return;

            var quote = await _qs.AddQuoteAsync(ctx.Guild.Id, ctx.User.Id, ctx.User.Username, keyword, text);

            await Response()
                  .Confirm(strs.quote_added_new(Format.Code(new kwum(quote.Id).ToString())))
                  .SendAsync();
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuoteEdit(kwum quoteId, [Leftover] string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var q = await _qs.EditQuoteAsync(ctx.User.Id, quoteId, text);

            if (q is not null)
            {
                await Response()
                      .Embed(CreateEmbed()
                                    .WithOkColor()
                                    .WithTitle(GetText(strs.quote_edited))
                                    .WithDescription($"#{quoteId}")
                                    .AddField(GetText(strs.trigger), q.Keyword)
                                    .AddField(GetText(strs.response),
                                        text.Length > 1024 ? GetText(strs.redacted_too_long) : text))
                      .SendAsync();
            }
            else
            {
                await Response().Error(strs.expr_no_found_id).SendAsync();
            }
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuoteDelete(kwum quoteId)
        {
            var hasManageMessages = ((IGuildUser)ctx.Message.Author).GuildPermissions.ManageMessages;

            var success = await _qs.DeleteQuoteAsync(ctx.Guild.Id, ctx.User.Id, hasManageMessages, quoteId);

            if (success)
                await Response().Confirm(strs.quote_deleted(quoteId)).SendAsync();
            else
                await Response().Error(strs.quotes_remove_none).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task QuoteDeleteAuthor(IUser user)
            => QuoteDeleteAuthor(user.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task QuoteDeleteAuthor(ulong userId)
        {
            var hasManageMessages = ((IGuildUser)ctx.Message.Author).GuildPermissions.ManageMessages;

            if (userId == ctx.User.Id || hasManageMessages)
            {
                var deleted = await _qs.DeleteAllAuthorQuotesAsync(ctx.Guild.Id, userId);
                await Response().Confirm(strs.quotes_deleted_count(deleted)).SendAsync();
            }
            else
            {
                await Response().Error(strs.insuf_perms_u).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task QuotesDeleteAll([Leftover] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return;

            await _qs.RemoveAllByKeyword(ctx.Guild.Id, keyword.ToUpperInvariant());

            await Response().Confirm(strs.quotes_deleted(Format.Bold(keyword))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task QuotesExport()
        {
            var quotes = await _qs.GetGuildQuotesAsync(ctx.Guild.Id);

            var exprsDict = quotes.GroupBy(x => x.Keyword)
                                  .ToDictionary(x => x.Key, x => x.Select(ExportedQuote.FromModel));

            var text = PREPEND_EXPORT + _exportSerializer.Serialize(exprsDict).UnescapeUnicodeCodePoints();

            await using var stream = await text.ToStream();
            await ctx.Channel.SendFileAsync(stream, "quote-export.yml");
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Ratelimit(300)]
        public async Task QuotesImport([Leftover] string? input = null)
        {
            input = input?.Trim();

            _ = ctx.Channel.TriggerTypingAsync();

            if (input is null)
            {
                var attachment = ctx.Message.Attachments.FirstOrDefault();
                if (attachment is null)
                {
                    await Response().Error(strs.expr_import_no_input).SendAsync();
                    return;
                }

                using var client = _http.CreateClient();
                input = await client.GetStringAsync(attachment.Url);

                if (string.IsNullOrWhiteSpace(input))
                {
                    await Response().Error(strs.expr_import_no_input).SendAsync();
                    return;
                }
            }

            var succ = await _qs.ImportQuotesAsync(ctx.Guild.Id, input);
            if (!succ)
            {
                await Response().Error(strs.expr_import_invalid_data).SendAsync();
                return;
            }

            await ctx.OkAsync();
        }
    }
}