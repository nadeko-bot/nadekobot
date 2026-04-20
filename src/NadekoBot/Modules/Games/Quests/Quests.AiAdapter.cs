using NadekoBot.AiAgent;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Games.Quests;

public sealed class QuestsAiAdapter(QuestService quests) : IAiToolGroup, INService
{
    public string GroupName => "quests";
    public string GroupDescription => "Daily quests for the invoking user.";

    [AiTool("list_my_quests", "Returns the invoking user's own daily quests and their progress. Only lists the caller's quests; it cannot look up other users.")]
    public async Task<MyQuestsDto> ListMyQuests(AiToolContext ctx)
    {
        var pairs = await quests.GetUserQuestsAsync(ctx.User.Id, DateTime.UtcNow);
        var entries = new List<QuestEntryDto>(pairs.Count);
        foreach (var (quest, uq) in pairs)
        {
            entries.Add(new(
                uq.QuestNumber,
                quest?.Name,
                quest?.Desc,
                quest?.ProgDesc,
                uq.Progress,
                quest?.RequiredAmount ?? 0,
                uq.IsCompleted));
        }

        return new(ctx.User.Id, DateTime.UtcNow.Date, entries);
    }
}

public sealed record MyQuestsDto(ulong UserId, DateTime Day, List<QuestEntryDto> Quests);

public readonly record struct QuestEntryDto(
    int QuestNumber,
    string? Name,
    string? Description,
    string? ProgressDescription,
    long Progress,
    long RequiredAmount,
    bool IsCompleted);
