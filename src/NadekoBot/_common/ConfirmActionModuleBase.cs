#nullable disable
namespace NadekoBot.Common;

public abstract class ConfirmActionModuleBase : NadekoModule
{
    protected async Task ConfirmActionInternalAsync(string name, Func<Task> action)
    {
        try
        {
            var embed = CreateEmbed()
                .WithTitle(GetText(strs.confirm_action))
                .WithDescription(name);

            if (!await PromptUserConfirmAsync(embed))
                return;

            await action();
            await ctx.OkAsync();
        }
        catch (Exception ex)
        {
            await Response().Error(ex.ToString()).SendAsync();
        }
    }
}