using NadekoBot.Modules.Utility.AiAgent.Prompts;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class PromptCommands(
        PromptLibrary promptLibrary,
        AiAgent.AiAgentConfigService configService,
        IHttpClientFactory httpFactory) : NadekoModule
    {
        [Cmd]
        [OwnerOnly]
        public async Task APrompts()
        {
            var modules = promptLibrary.ListModules();
            var config = configService.Data;
            var enabledSet = config.EnabledModules is { Count: > 0 }
                ? config.EnabledModules.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;

            var eb = CreateEmbed()
                .WithTitle(GetText(strs.aprompts_title))
                .WithOkColor();

            var soul = promptLibrary.GetSoul();
            var operatorDoc = promptLibrary.GetOperatorDoc();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"**SOUL.md** - {soul.Length} chars");
            sb.AppendLine($"**OPERATOR.md** - {operatorDoc.Length} chars");
            sb.AppendLine();
            sb.AppendLine("**Modules:**");

            if (modules.Count == 0)
            {
                sb.AppendLine("*(none)*");
            }
            else
            {
                foreach (var name in modules)
                {
                    var isEnabled = enabledSet is null || enabledSet.Contains(name);
                    var icon = isEnabled ? "\u2705" : "\u274C";
                    sb.AppendLine($"{icon} `{name}`");
                }
            }

            eb.WithDescription(sb.ToString());
            await Response().Embed(eb).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task APromptShow([Leftover] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                await Response().Error(strs.aprompt_no_path).SendAsync();
                return;
            }

            var normalizedPath = NormalizePromptPathInternal(path.Trim());
            var (content, _) = promptLibrary.ReadRaw(normalizedPath);

            if (content is null)
            {
                await Response().Error(strs.aprompt_not_found(normalizedPath)).SendAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                await Response().Pending(strs.aprompt_empty(normalizedPath)).SendAsync();
                return;
            }

            // If too long for a Discord message, DM it in chunks
            if (content.Length > 1900)
            {
                try
                {
                    var dm = await ctx.User.CreateDMChannelAsync();
                    const int chunkSize = 1900;
                    for (var i = 0; i < content.Length; i += chunkSize)
                    {
                        var end = Math.Min(i + chunkSize, content.Length);
                        await dm.SendMessageAsync($"```md\n{content[i..end]}\n```");
                    }

                    await Response().Confirm(strs.aprompt_sent_dm).SendAsync();
                }
                catch
                {
                    await Response().Error(strs.aprompt_dm_failed).SendAsync();
                }
            }
            else
            {
                await Response().Confirm($"```md\n{content}\n```").SendAsync();
            }
        }

        [Cmd]
        [OwnerOnly]
        public async Task APromptEdit(string path, [Leftover] string? content = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                await Response().Error(strs.aprompt_no_path).SendAsync();
                return;
            }

            // Check for attached .md file
            if (string.IsNullOrWhiteSpace(content))
            {
                var attachment = ctx.Message.Attachments
                    .FirstOrDefault(a => a.Filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

                if (attachment is null)
                {
                    await Response().Error(strs.aprompt_no_content).SendAsync();
                    return;
                }

                using var http = httpFactory.CreateClient();
                content = await http.GetStringAsync(attachment.Url);
            }

            // Normalize path: "SOUL" -> "SOUL.md", "discord-formatting" -> "modules/discord-formatting.md"
            var normalizedPath = NormalizePromptPathInternal(path.Trim());

            if (promptLibrary.TryWrite(normalizedPath, content, out var error))
            {
                await promptLibrary.ReloadAsync();
                await Response().Confirm(strs.aprompt_updated(normalizedPath)).SendAsync();
            }
            else
            {
                await Response().Error(strs.aprompt_write_failed(error)).SendAsync();
            }
        }

        [Cmd]
        [OwnerOnly]
        public async Task APromptReload()
        {
            await promptLibrary.ReloadAsync();
            await Response().Confirm(strs.aprompt_reloaded).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task APromptModule([Leftover] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                await Response().Error(strs.aprompt_no_module).SendAsync();
                return;
            }

            name = name.Trim();

            // Check if this module actually exists
            var allModules = promptLibrary.ListModules();
            if (!allModules.Any(m => m.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
            {
                await Response().Error(strs.aprompt_module_not_found(name)).SendAsync();
                return;
            }

            var config = configService.Data;
            var current = config.EnabledModules;

            if (current.Count == 0)
            {
                // Currently "all enabled" - switching to explicit list minus this one
                var newList = allModules
                    .Where(m => !m.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    .ToList();

                configService.ModifyConfig(c => c.EnabledModules = newList);
                await Response().Confirm(strs.aprompt_module_disabled(name)).SendAsync();
            }
            else
            {
                var existing = current.FirstOrDefault(m =>
                    m.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                if (existing is not null)
                {
                    // Remove it (disable)
                    configService.ModifyConfig(c =>
                        c.EnabledModules = c.EnabledModules
                            .Where(m => !m.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                            .ToList());
                    await Response().Confirm(strs.aprompt_module_disabled(name)).SendAsync();
                }
                else
                {
                    // Add it (enable)
                    configService.ModifyConfig(c =>
                    {
                        c.EnabledModules = [..c.EnabledModules, name];
                    });
                    await Response().Confirm(strs.aprompt_module_enabled(name)).SendAsync();
                }
            }
        }

        [Cmd]
        [OwnerOnly]
        public async Task APromptPath()
        {
            var fullPath = Path.GetFullPath(PromptLibrary.DEFAULT_PROMPTS_DIR);
            await Response().Confirm(strs.aprompt_path(fullPath)).SendAsync();
        }

        private static string NormalizePromptPathInternal(string input)
        {
            if (input.Equals("SOUL", StringComparison.InvariantCultureIgnoreCase)
                || input.Equals("SOUL.md", StringComparison.InvariantCultureIgnoreCase))
                return "SOUL.md";
            if (input.Equals("OPERATOR", StringComparison.InvariantCultureIgnoreCase)
                || input.Equals("OPERATOR.md", StringComparison.InvariantCultureIgnoreCase))
                return "OPERATOR.md";

            // Assume it's a module name
            if (!input.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                input += ".md";

            if (!input.StartsWith("modules/", StringComparison.OrdinalIgnoreCase)
                && !input.StartsWith("modules\\", StringComparison.OrdinalIgnoreCase))
                input = $"modules/{input}";

            return input;
        }
    }
}
