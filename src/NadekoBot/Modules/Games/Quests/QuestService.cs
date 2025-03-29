using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Games.Quests;

public sealed class QuestService(
    DbService db,
    IBotCache botCache,
    IMessageSenderService sender,
    DiscordSocketClient client
) : INService, IExecPreCommand
{
    private readonly IQuest[] _availableQuests =
    [
        new HangmanWinQuest(),
        new PlantPickQuest(),
        new BetQuest(),
        new BetFlowersQuest(),
        new GiftWaifuQuest(),
        new CatchFishQuest(),
        new SetPixelsQuest(),
        new JoinAnimalRaceQuest(),
        new BankerQuest(),
        new CheckLeaderboardsQuest(),
        new WellInformedQuest(),
    ];

    private const int MAX_QUESTS_PER_DAY = 3;

    private TypedKey<bool> UserHasQuestsKey(ulong userId)
        => new($"daily:generated:{userId}");

    private TypedKey<bool> UserCompletedDailiesKey(ulong userId)
        => new($"daily:completed:{userId}");


    public Task ReportActionAsync(
        ulong userId,
        QuestEventType eventType,
        Dictionary<string, string>? metadata = null)
    {
        // don't block any caller

        _ = Task.Run(async () =>
        {
            metadata ??= new();
            var now = DateTime.UtcNow;

            var alreadyDone = await botCache.GetAsync(UserCompletedDailiesKey(userId));
            if (alreadyDone.IsT0)
                return;

            var userQuests = await GetUserQuestsAsync(userId, now);

            foreach (var (q, uq) in userQuests)
            {
                // deleted quest
                if (q is null)
                    continue;

                // user already completed or incorrect event
                if (uq.IsCompleted || q.EventType != eventType)
                    continue;

                var newProgress = q.TryUpdateProgress(metadata, uq.Progress);

                // user already did that part of the quest
                if (newProgress == uq.Progress)
                    continue;

                var isCompleted = newProgress >= q.RequiredAmount;

                await using var uow = db.GetDbContext();
                await uow.GetTable<UserQuest>()
                    .Where(x => x.UserId == userId && x.QuestId == q.QuestId && x.QuestNumber == uq.QuestNumber)
                    .Set(x => x.Progress, newProgress)
                    .Set(x => x.IsCompleted, isCompleted)
                    .UpdateAsync();

                uq.IsCompleted = isCompleted;

                if (userQuests.All(x => x.UserQuest.IsCompleted))
                {
                    var timeUntilTomorrow = now.Date.AddDays(1) - DateTime.UtcNow;
                    if (!await botCache.AddAsync(
                            UserCompletedDailiesKey(userId),
                            true,
                            expiry: timeUntilTomorrow))
                        return;

                    try
                    {
                        var user = await client.GetUserAsync(userId);
                        await sender
                            .Response(user)
                            .Confirm(strs.dailies_done)
                            .SendAsync();
                    }
                    catch
                    {
                        // we don't really care if the user receives it
                    }

                    break;
                }
            }
        });

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<(IQuest? Quest, UserQuest UserQuest)>> GetUserQuestsAsync(
        ulong userId,
        DateTime now)
    {
        var today = now.Date;
        await EnsureUserDailiesAsync(userId, today);

        await using var uow = db.GetDbContext();
        var quests = await uow.GetTable<UserQuest>()
            .Where(x => x.UserId == userId && x.DateAssigned == today)
            .ToListAsync();

        return quests
            .Select(x => (_availableQuests.FirstOrDefault(q => q.QuestId == x.QuestId), x))
            .Select(x => x!)
            .ToList();
    }

    private async Task EnsureUserDailiesAsync(ulong userId, DateTime date)
    {
        var today = date.Date;
        var timeUntilTomorrow = today.AddDays(1) - DateTime.UtcNow;
        if (!await botCache.AddAsync(UserHasQuestsKey(userId), true, expiry: timeUntilTomorrow, overwrite: false))
            return;

        await using var uow = db.GetDbContext();
        var newQuests = GenerateDailyQuestsAsync();
        for (var i = 0; i < MAX_QUESTS_PER_DAY; i++)
        {
            await uow.GetTable<UserQuest>()
                .InsertOrUpdateAsync(() => new()
                    {
                        UserId = userId,
                        QuestNumber = i,
                        DateAssigned = today,

                        IsCompleted = false,
                        QuestId = newQuests[i].QuestId,
                        Progress = 0,
                    },
                    old => new()
                    {
                    },
                    () => new()
                    {
                        UserId = userId,
                        QuestNumber = i,
                        DateAssigned = today
                    });
        }
    }

    private IReadOnlyList<IQuest> GenerateDailyQuestsAsync()
    {
        return _availableQuests
            .ToList()
            .Shuffle()
            .Take(MAX_QUESTS_PER_DAY)
            .ToList();
    }

    public int Priority
        => int.MinValue;

    public async Task<bool> ExecPreCommandAsync(ICommandContext context, string moduleName, CommandInfo command)
    {
        var cmdName = command.Name.ToLowerInvariant();

        await ReportActionAsync(
            context.User.Id,
            QuestEventType.CommandUsed,
            new()
            {
                { "name", cmdName }
            });

        return false;
    }

    public async Task<bool> UserCompletedDailies(ulong userId)
    {
        var result = await botCache.GetAsync(UserCompletedDailiesKey(userId));

        return result.IsT0;
    }
}