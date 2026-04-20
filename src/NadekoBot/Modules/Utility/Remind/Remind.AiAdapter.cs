using NadekoBot.AiAgent;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility.Remind;

public sealed class RemindersAiAdapter(RemindService reminders) : IAiToolGroup, INService
{
    public string GroupName => "reminders";
    public string GroupDescription => "Personal reminders for the invoking user.";

    [AiTool("list_my_reminders", "Returns the invoking user's own reminders. Only lists reminders for the user who triggered this agent; it cannot look up other users' reminders.")]
    public async Task<UserRemindersDto> ListMyReminders(
        AiToolContext ctx,
        [AiParam("Page of reminders to fetch, 1 = first page")] int page = 1)
    {
        page = Math.Max(1, page);
        var list = await reminders.GetUserReminders(page, ctx.User.Id);

        var entries = new List<ReminderDto>(list.Count);
        foreach (var r in list)
        {
            entries.Add(new(
                r.Id,
                r.When,
                r.ChannelId,
                r.GuildId,
                r.Message,
                r.IsPrivate,
                r.Type.ToString()));
        }

        return new(ctx.User.Id, page, entries);
    }
}

public sealed record UserRemindersDto(ulong UserId, int Page, List<ReminderDto> Reminders);

public readonly record struct ReminderDto(
    int Id,
    DateTime When,
    ulong ChannelId,
    ulong GuildId,
    string? Message,
    bool IsPrivate,
    string Type);
