#nullable enable
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;
using NadekoBot.Modules.Owner.Services;
using SQLitePCL;

namespace NadekoBot.Modules.Owner;

public partial class Owner
{
    [Group]
    [OwnerOnly]
    [NoAiAgent]
    public partial class SqlCommands(DangerousCommandsService ds) : NadekoModule
    {
        // Under Discord's upload limit for bots.
        private const int MAX_CSV_BYTES = 8 * 1024 * 1024;
        private const int PAGE_SIZE = 20;
        private const string FENCE = "```";
        private const int MAX_ERROR_CHARS = 500;

        // The pages can show no more than this, so the bot keeps no more in memory while the pages stay open.
        private const int MAX_EMBED_CHARS = DangerousCommandsService.MAX_SELECT_ROWS / PAGE_SIZE * EmbedBuilder.MaxDescriptionLength;

        [Cmd]
        [OwnerOnly]
        [NoAiAgent]
        [RequireContext(ContextType.DM)]
        public async Task SqlSelect([Leftover] string sql)
        {
            if (await RunInternalAsync(sql, MAX_EMBED_CHARS) is not { } result)
                return;

            if (result.Truncated)
                await Response().Pending(strs.sql_select_truncated(DangerousCommandsService.MAX_SELECT_ROWS)).SendAsync();

            var title = string.Join(" | ", result.ColumnNames).TrimTo(EmbedBuilder.MaxTitleLength);

            await Response()
                  .Paginated()
                  .Items(result.Results)
                  .PageSize(PAGE_SIZE)
                  .Page((items, _) =>
                  {
                      if (items.Count == 0)
                          return CreateEmbed().WithErrorColor().WithTitle(title).WithDescription("-");

                      return CreateEmbed()
                             .WithOkColor()
                             .WithTitle(title)
                             .WithDescription(ToCodeBlockInternal(items));
                  })
                  .SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        [NoAiAgent]
        [RequireContext(ContextType.DM)]
        public async Task SqlSelectCsv([Leftover] string sql)
        {
            if (await RunInternalAsync(sql, DangerousCommandsService.MAX_SELECT_CHARS) is not { } result)
                return;

            var truncated = result.Truncated;
            using var ms = new MemoryStream();
            await using (var sw = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true))
            await using (var csv = new CsvWriter(sw, new CsvConfiguration(CultureInfo.InvariantCulture)
                   {
                       InjectionOptions = InjectionOptions.Escape
                   }))
            {
                foreach (var cn in result.ColumnNames)
                    csv.WriteField(cn);

                await csv.NextRecordAsync();

                foreach (var row in result.Results)
                {
                    var before = ms.Length;
                    foreach (var field in row)
                        csv.WriteField(field);

                    await csv.NextRecordAsync();
                    await csv.FlushAsync();

                    if (ms.Length > MAX_CSV_BYTES)
                    {
                        ms.SetLength(before);
                        truncated = true;
                        break;
                    }
                }
            }

            ms.Position = 0;
            await ctx.Channel.SendFileAsync(ms,
                $"query_result_{DateTime.UtcNow.Ticks}.csv",
                truncated ? GetText(strs.sql_select_truncated(DangerousCommandsService.MAX_SELECT_ROWS)) : null);
        }

        private async Task<DangerousCommandsService.SelectResult?> RunInternalAsync(string sql, int maxChars)
        {
            try
            {
                return await Task.Run(() => ds.SelectSql(sql, maxChars));
            }
            catch (DangerousCommandsService.QueryBusyException)
            {
                await Response().Error(strs.sql_select_busy).SendAsync();
            }
            catch (DangerousCommandsService.NotOneStatementException)
            {
                await Response().Error(strs.sql_select_one_statement).SendAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT)
            {
                await Response()
                      .Error(strs.sql_select_timeout((int)DangerousCommandsService.SelectTimeLimit.TotalSeconds))
                      .SendAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_TOOBIG)
            {
                await Response()
                      .Error(strs.sql_select_too_big(OwnerSqlConnection.READ_MAX_VALUE_BYTES / 1024))
                      .SendAsync();
            }
            catch (SqliteException ex)
            {
                await Response().Error(strs.sql_select_failed($"{FENCE}\n{EscapeInternal(ex.Message.TrimTo(MAX_ERROR_CHARS) ?? string.Empty)}\n{FENCE}")).SendAsync();
            }

            return null;
        }

        // No two backticks stay next to each other, so the text cannot end the code block.
        private static string EscapeInternal(string text)
            => text.Replace("`", "`\u200b");

        // A code block shows the values as plain text, without markdown or links.
        private static string ToCodeBlockInternal(IReadOnlyList<string[]> rows)
        {
            var max = EmbedBuilder.MaxDescriptionLength - (FENCE.Length * 2) - 2;
            var sb = new StringBuilder(FENCE).Append('\n');
            var start = sb.Length;
            for (var i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                    sb.Append('\n');

                for (var j = 0; j < rows[i].Length; j++)
                {
                    if (j > 0)
                        sb.Append(" | ");

                    sb.Append(EscapeInternal(rows[i][j]));
                }

                if (sb.Length - start > max)
                {
                    sb.Length = start + max - 3;
                    sb.Append("...");
                    break;
                }
            }

            return sb.Append('\n').Append(FENCE).ToString();
        }
    }
}
