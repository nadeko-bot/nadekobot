#nullable disable

using NadekoBot.Db.Models;

namespace NadekoBot.Modules.NadekoExpressions;

[Name("Expressions")]
public partial class NadekoExpressions : NadekoModule<NadekoExpressionsService>
{
    public enum All
    {
        All
    }

    private readonly IBotCreds _creds;
    private readonly IHttpClientFactory _clientFactory;

    public NadekoExpressions(IBotCreds creds, IHttpClientFactory clientFactory)
    {
        _creds = creds;
        _clientFactory = clientFactory;
    }

    private bool AdminInGuildOrOwnerInDm()
        => (ctx.Guild is null && _creds.IsOwner(ctx.User))
           || (ctx.Guild is not null && ((IGuildUser)ctx.User).GuildPermissions.Administrator);

    private async Task ExprAddInternalAsync(string key, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var ex = await _service.AddAsync(ctx.Guild?.Id, key, message);

        await Response()
              .Embed(CreateEmbed()
                            .WithOkColor()
                            .WithTitle(GetText(strs.expr_new))
                            .WithDescription($"#{new kwum(ex.Id)}")
                            .AddField(GetText(strs.trigger), key)
                            .AddField(GetText(strs.response),
                                message.Length > 1024 ? GetText(strs.redacted_too_long) : message))
              .SendAsync();
    }

    [Cmd]
    [UserPerm(GuildPerm.Administrator)]
    public async Task ExprToggleGlobal()
    {
        var result = await _service.ToggleGlobalExpressionsAsync(ctx.Guild.Id);
        if (result)
            await Response().Confirm(strs.expr_global_disabled).SendAsync();
        else
            await Response().Confirm(strs.expr_global_enabled).SendAsync();
    }

    [Cmd]
    [UserPerm(GuildPerm.Administrator)]
    public async Task ExprAddServer(string trigger, [Leftover] string response)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(trigger))
        {
            return;
        }

        await ExprAddInternalAsync(trigger, response);
    }


    [Cmd]
    public async Task ExprAdd(string trigger, [Leftover] string response)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(trigger))
        {
            return;
        }

        if (!AdminInGuildOrOwnerInDm())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

        await ExprAddInternalAsync(trigger, response);
    }

    [Cmd]
    public async Task ExprEdit(kwum id, [Leftover] string message)
    {
        var channel = ctx.Channel as ITextChannel;
        if (string.IsNullOrWhiteSpace(message) || id < 0)
        {
            return;
        }

        if (!IsValidExprEditor())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

        var ex = await _service.EditAsync(ctx.Guild?.Id, id, message);
        if (ex is not null)
        {
            await Response()
                  .Embed(CreateEmbed()
                                .WithOkColor()
                                .WithTitle(GetText(strs.expr_edited))
                                .WithDescription($"#{id}")
                                .AddField(GetText(strs.trigger), ex.Trigger)
                                .AddField(GetText(strs.response),
                                    message.Length > 1024 ? GetText(strs.redacted_too_long) : message))
                  .SendAsync();
        }
        else
        {
            await Response().Error(strs.expr_no_found_id).SendAsync();
        }
    }

    private bool IsValidExprEditor()
        => (ctx.Guild is not null && ((IGuildUser)ctx.User).GuildPermissions.Administrator)
           || (ctx.Guild is null && _creds.IsOwner(ctx.User));

    [Cmd]
    [Priority(1)]
    public async Task ExprList(int page = 1)
    {
        if (--page < 0 || page > 999)
        {
            return;
        }

        var allExpressions = _service.GetExpressionsFor(ctx.Guild?.Id)
                                     .OrderBy(x => x.Trigger)
                                     .ToArray();

        if (!allExpressions.Any())
        {
            await Response().Error(strs.expr_no_found).SendAsync();
            return;
        }

        await Response()
              .Paginated()
              .Items(allExpressions)
              .PageSize(20)
              .CurrentPage(page)
              .Page((exprs, _) =>
              {
                  var desc = exprs
                             .Select(ex => $"{(ex.ContainsAnywhere ? "🗯" : "◾")}"
                                           + $"{(ex.DmResponse ? "✉" : "◾")}"
                                           + $"{(ex.AutoDeleteTrigger ? "❌" : "◾")}"
                                           + $"`{(kwum)ex.Id}` {ex.Trigger}"
                                           + (string.IsNullOrWhiteSpace(ex.Reactions)
                                               ? string.Empty
                                               : " // " + string.Join(" ", ex.GetReactions())))
                             .Join('\n');

                  return CreateEmbed().WithOkColor().WithTitle(GetText(strs.expressions)).WithDescription(desc);
              })
              .SendAsync();
    }

    [Cmd]
    public async Task ExprShow(kwum id)
    {
        var found = _service.GetExpression(ctx.Guild?.Id, id);

        if (found is null)
        {
            await Response().Error(strs.expr_no_found_id).SendAsync();
            return;
        }

        var inter = CreateEditInteraction(id, found);

        await Response()
              .Interaction(IsValidExprEditor() ? inter : null)
              .Embed(CreateEmbed()
                            .WithOkColor()
                            .WithDescription($"#{id}")
                            .AddField(GetText(strs.trigger), found.Trigger.TrimTo(1024))
                            .AddField(GetText(strs.response),
                                found.Response.TrimTo(1000).Replace("](", "]\\(")))
              .SendAsync();
    }

    private NadekoInteractionBase CreateEditInteraction(kwum id, NadekoExpression found)
    {
        var modal = new ModalBuilder()
                    .WithCustomId("expr:edit_modal")
                    .WithTitle($"Edit expression {id}")
                    .AddTextInput(new TextInputBuilder()
                                  .WithLabel(GetText(strs.response))
                                  .WithValue(found.Response)
                                  .WithMinLength(1)
                                  .WithCustomId("expr:edit_modal:response")
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

                await ExprEdit(id, msg);
            }
        );
        return inter;
    }

    public async Task ExprDeleteInternalAsync(kwum id)
    {
        var ex = await _service.DeleteAsync(ctx.Guild?.Id, id);

        if (ex is not null)
        {
            await Response()
                  .Embed(CreateEmbed()
                                .WithOkColor()
                                .WithTitle(GetText(strs.expr_deleted))
                                .WithDescription($"#{id}")
                                .AddField(GetText(strs.trigger), ex.Trigger.TrimTo(1024))
                                .AddField(GetText(strs.response), ex.Response.TrimTo(1024)))
                  .SendAsync();
        }
        else
        {
            await Response().Error(strs.expr_no_found_id).SendAsync();
        }
    }

    [Cmd]
    [UserPerm(GuildPerm.Administrator)]
    [RequireContext(ContextType.Guild)]
    public async Task ExprDeleteServer(kwum id)
        => await ExprDeleteInternalAsync(id);

    [Cmd]
    public async Task ExprDelete(kwum id)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

        await ExprDeleteInternalAsync(id);
    }

    [Cmd]
    public async Task ExprReact(kwum id, params string[] emojiStrs)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

        var ex = _service.GetExpression(ctx.Guild?.Id, id);
        if (ex is null)
        {
            await Response().Error(strs.expr_no_found_id).SendAsync();
            return;
        }

        if (emojiStrs.Length == 0)
        {
            await _service.ResetExprReactions(ctx.Guild?.Id, id);
            await Response().Confirm(strs.expr_reset(Format.Bold(id.ToString()))).SendAsync();
            return;
        }

        var succ = new List<string>();
        foreach (var emojiStr in emojiStrs)
        {
            var emote = emojiStr.ToIEmote();

            // i should try adding these emojis right away to the message, to make sure the bot can react with these emojis. If it fails, skip that emoji
            try
            {
                await ctx.Message.AddReactionAsync(emote);
                await Task.Delay(100);
                succ.Add(emojiStr);

                if (succ.Count >= 3)
                {
                    break;
                }
            }
            catch { }
        }

        if (succ.Count == 0)
        {
            await Response().Error(strs.invalid_emojis).SendAsync();
            return;
        }

        await _service.SetExprReactions(ctx.Guild?.Id, id, succ);


        await Response()
              .Confirm(strs.expr_set(Format.Bold(id.ToString()),
                  succ.Select(static x => x.ToString()).Join(", ")))
              .SendAsync();
    }

    [Cmd]
    public Task ExprCa(kwum id)
        => InternalExprEdit(id, ExprField.ContainsAnywhere);

    [Cmd]
    public Task ExprDm(kwum id)
        => InternalExprEdit(id, ExprField.DmResponse);

    [Cmd]
    public Task ExprAd(kwum id)
        => InternalExprEdit(id, ExprField.AutoDelete);

    [Cmd]
    public Task ExprAt(kwum id)
        => InternalExprEdit(id, ExprField.AllowTarget);

    [Cmd]
    [OwnerOnly]
    public async Task ExprsReload()
    {
        await _service.TriggerReloadExpressions();

        await ctx.OkAsync();
    }

    private async Task InternalExprEdit(kwum id, ExprField option)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

        var (success, newVal) = await _service.ToggleExprOptionAsync(ctx.Guild?.Id, id, option);
        if (!success)
        {
            await Response().Error(strs.expr_no_found_id).SendAsync();
            return;
        }

        if (newVal)
        {
            await Response()
                  .Confirm(strs.option_enabled(Format.Code(option.ToString()),
                      Format.Code(id.ToString())))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.option_disabled(Format.Code(option.ToString()),
                      Format.Code(id.ToString())))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    public async Task ExprClear()
    {
        if (await PromptUserConfirmAsync(CreateEmbed()
                                                .WithTitle("Expression clear")
                                                .WithDescription("This will delete all expressions on this server.")))
        {
            var count = _service.DeleteAllExpressions(ctx.Guild.Id);
            await Response().Confirm(strs.exprs_cleared(count)).SendAsync();
        }
    }

    [Cmd]
    public async Task ExprsExport()
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

        _ = ctx.Channel.TriggerTypingAsync();

        var serialized = _service.ExportExpressions(ctx.Guild?.Id);
        await using var stream = await serialized.ToStream();
        await ctx.User.SendFileAsync(stream, $"exprs-export_{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}_{(ctx.Guild?.Id.ToString() ?? "global")}.yml");
    }

    [Cmd]
    [Ratelimit(300)]
    public async Task ExprsImport([Leftover] string input = null)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await Response().Error(strs.expr_insuff_perms).SendAsync();
            return;
        }

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

            using var client = _clientFactory.CreateClient();
            input = await client.GetStringAsync(attachment.Url);

            if (string.IsNullOrWhiteSpace(input))
            {
                await Response().Error(strs.expr_import_no_input).SendAsync();
                return;
            }
        }

        var succ = await _service.ImportExpressionsAsync(ctx.Guild?.Id, input);
        if (!succ)
        {
            await Response().Error(strs.expr_import_invalid_data).SendAsync();
            return;
        }

        await ctx.OkAsync();
    }
}