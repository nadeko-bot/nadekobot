using NadekoBot.Modules.Utility.AiAgent.Prompts;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class PromptCommands(
        DiscordSocketClient client,
        PromptLibrary promptLibrary) : NadekoModule
    {
        [Cmd]
        [OwnerOnly]
        public async Task AiPrompt()
        {
            var soul = promptLibrary.GetSoul();
            var operatorDoc = promptLibrary.GetOperatorDoc();

            var eb = CreateEmbed()
                .WithTitle(GetText(strs.aiprompt_title))
                .WithDescription(GetText(strs.aiprompt_desc(soul.Length, operatorDoc.Length)))
                .WithOkColor();

            var soulBtn = new ButtonBuilder()
                .WithCustomId($"aiprompt:soul:{Guid.NewGuid():N}")
                .WithLabel("SOUL")
                .WithStyle(ButtonStyle.Primary);

            var operatorBtn = new ButtonBuilder()
                .WithCustomId($"aiprompt:operator:{Guid.NewGuid():N}")
                .WithLabel("OPERATOR")
                .WithStyle(ButtonStyle.Primary);

            var soulHandler = new NadekoButtonInteractionHandler(
                client,
                ctx.User.Id,
                soulBtn,
                async smc =>
                {
                    await smc.DeferAsync();
                    await ShowAndOfferEditAsync(PromptKind.Soul);
                },
                onlyAuthor: true,
                singleUse: true,
                clearAfter: true);

            var operatorHandler = new NadekoButtonInteractionHandler(
                client,
                ctx.User.Id,
                operatorBtn,
                async smc =>
                {
                    await smc.DeferAsync();
                    await ShowAndOfferEditAsync(PromptKind.Operator);
                },
                onlyAuthor: true,
                singleUse: true,
                clearAfter: true);

            var components = new ComponentBuilder()
                .WithButton(soulBtn)
                .WithButton(operatorBtn);

            var msg = await ctx.Channel.SendMessageAsync(
                embed: eb.Build(),
                components: components.Build());

            await Task.WhenAll(soulHandler.RunAsync(msg), operatorHandler.RunAsync(msg));
        }

        [Cmd]
        [OwnerOnly]
        public Task AiPrompt(PromptKind kind)
            => ShowAndOfferEditAsync(kind);

        private async Task ShowAndOfferEditAsync(PromptKind kind)
        {
            var content = promptLibrary.Read(kind);
            var label = kind == PromptKind.Soul ? "SOUL.md" : "OPERATOR.md";

            var eb = CreateEmbed()
                .WithTitle(label)
                .WithOkColor();

            if (string.IsNullOrWhiteSpace(content))
                eb.WithDescription(GetText(strs.aiprompt_empty));
            else if (content.Length <= 4000)
                eb.WithDescription($"```md\n{content}\n```");
            else
                eb.WithDescription($"```md\n{content[..3990]}…\n```");

            var modal = new ModalBuilder()
                .WithCustomId($"aiprompt:edit:{kind}:{Guid.NewGuid():N}")
                .WithTitle($"Edit {label}")
                .AddTextInput(
                    label,
                    $"aiprompt:edit:{kind}:value",
                    TextInputStyle.Paragraph,
                    minLength: 0,
                    maxLength: 4000,
                    value: content.Length > 4000 ? content[..4000] : content);

            var editBtn = new ButtonBuilder()
                .WithEmote(Emoji.Parse("📝"))
                .WithLabel("Edit")
                .WithStyle(ButtonStyle.Primary)
                .WithCustomId($"aiprompt:editbtn:{kind}:{Guid.NewGuid():N}");

            var inter = _inter.Create(
                ctx.User.Id,
                editBtn,
                modal,
                async sm =>
                {
                    var newContent = sm.Data.Components.FirstOrDefault()?.Value ?? string.Empty;
                    await sm.DeferAsync();
                    await ApplyEditAsync(kind, newContent, label);
                });

            await Response().Embed(eb).Interaction(inter).SendAsync();
        }

        private async Task ApplyEditAsync(PromptKind kind, string content, string label)
        {
            if (promptLibrary.TryWrite(kind, content, out var error))
            {
                await promptLibrary.ReloadAsync();
                await Response().Confirm(strs.aiprompt_updated(label)).SendAsync();
            }
            else
            {
                await Response().Error(strs.aiprompt_write_failed(error)).SendAsync();
            }
        }
    }
}
