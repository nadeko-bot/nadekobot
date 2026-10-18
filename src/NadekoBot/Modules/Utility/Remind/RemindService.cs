#nullable disable
using System.Globalization;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.Services;

public class RemindService : INService, IReadyExecutor, IRemindService
{
    public const int MAX_REMINDERS_PER_USER = 10;
    public const int MAX_REMINDERS_PER_SERVER = 50;

    private readonly Regex _regex =
        new(
            @"^(?:(?:at|on(?:\sthe)?)?\s*(?<date>(?:\d{2}:\d{2}\s)?\d{1,2}\.\d{1,2}(?:\.\d{2,4})?)|(?:in\s?)?\s*(?:(?<mo>\d+)(?:\s?(?:months?|mos?),?))?(?:(?:\sand\s|\s*)?(?<w>\d+)(?:\s?(?:weeks?|w),?))?(?:(?:\sand\s|\s*)?(?<d>\d+)(?:\s?(?:days?|d),?))?(?:(?:\sand\s|\s*)?(?<h>\d+)(?:\s?(?:hours?|h),?))?(?:(?:\sand\s|\s*)?(?<m>\d+)(?:\s?(?:minutes?|mins?|m),?))?)\s+(?:to:?\s+)?(?<what>(?:\r\n|[\r\n]|.)+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly IBotCreds _creds;
    private readonly IMessageSenderService _sender;
    private readonly CultureInfo _culture;

    private TaskCompletionSource<bool> _tcs;

    public RemindService(
        DiscordSocketClient client,
        DbService db,
        IBotCreds creds,
        IMessageSenderService sender)
    {
        _client = client;
        _db = db;
        _creds = creds;
        _sender = sender;

        try
        {
            _culture = new CultureInfo("en-GB");
        }
        catch
        {
            _culture = CultureInfo.InvariantCulture;
        }
    }

    private static readonly TimeSpan _loopRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _maxWait = TimeSpan.FromDays(30);

    // Reminders due within this window run together, so the loop never waits for less than it.
    private static readonly TimeSpan _dueWindow = TimeSpan.FromSeconds(2);

    public Task OnReadyAsync()
    {
        _ = Task.Run(ReminderLoopInternalAsync);
        return Task.CompletedTask;
    }

    private async Task ReminderLoopInternalAsync()
    {
        while (true)
        {
            if (!await OnReminderLoopTickInternalAsync())
                await Task.Delay(_loopRetryDelay);
        }
    }

    // Returns false after an error, so the loop waits before the next try.
    private async Task<bool> OnReminderLoopTickInternalAsync()
    {
        try
        {
            var reminders = await GetRemindersBeforeAsync();
            if (reminders.Count == 0)
                return true;

            Log.Information("Executing {ReminderCount} reminders", reminders.Count);

            // make groups of 5, with 1.5 second in between each one to ensure against ratelimits
            foreach (var group in reminders.Chunk(5))
            {
                var executedReminders = group.ToList();
                await executedReminders.Select(ReminderTimerAction).WhenAll();
                await Task.Delay(1500);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in reminder loop: {ErrorMessage}", ex.Message);
            return false;
        }

        return true;
    }

    private async Task RemoveReminders(IEnumerable<int> reminders)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<Reminder>()
            .DeleteAsync(x => reminders.Contains(x.Id));

        await uow.SaveChangesAsync();
    }

    // Zero means the reminder is due: it falls in the window which the delete takes.
    public static TimeSpan GetReminderWait(DateTime when, DateTime now)
    {
        if (when <= now + _dueWindow)
            return TimeSpan.Zero;

        var wait = when - now;
        return wait > _maxWait ? _maxWait : wait;
    }

    private async Task<IReadOnlyList<Reminder>> GetRemindersBeforeAsync()
    {
        while (true)
        {
            _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            DateTime? earliest;
            await using (var uow = _db.GetDbContext())
            {
                earliest = await uow.GetTable<Reminder>()
                    .Where(Queries.GuildOnShard<Reminder>(x => x.GuildId,
                        _creds.TotalShards,
                        _client.ShardId))
                    .OrderBy(x => x.When)
                    .Select(x => (DateTime?)x.When)
                    .FirstOrDefaultAsyncLinqToDB();
            }

            if (earliest is null)
            {
                await _tcs.Task;
                continue;
            }

            var now = DateTime.UtcNow;
            var wait = GetReminderWait(earliest.Value, now);
            if (wait > TimeSpan.Zero)
            {
                await Task.WhenAny(Task.Delay(wait), _tcs.Task);
                continue;
            }

            var cutoff = now + _dueWindow;
            await using (var uow = _db.GetDbContext())
            {
                return await uow.GetTable<Reminder>()
                    .Where(Queries.GuildOnShard<Reminder>(x => x.GuildId,
                        _creds.TotalShards,
                        _client.ShardId))
                    .Where(x => x.When <= cutoff)
                    .DeleteWithOutputAsync();
            }
        }
    }

    public bool TryParseRemindMessage(string input, out RemindObject obj)
    {
        var m = _regex.Match(input);

        obj = default;
        if (m.Length == 0)
            return false;

        var values = new Dictionary<string, int>();

        var what = m.Groups["what"].Value;

        if (string.IsNullOrWhiteSpace(what))
        {
            Log.Warning("No message provided for the reminder");
            return false;
        }

        TimeSpan ts;

        var dateString = m.Groups["date"].Value;
        if (!string.IsNullOrWhiteSpace(dateString))
        {
            var now = DateTime.UtcNow;

            if (!DateTime.TryParse(dateString, _culture, DateTimeStyles.None, out var dt))
            {
                Log.Warning("Invalid remind datetime format");
                return false;
            }

            if (now >= dt)
            {
                Log.Warning("That remind time has already passed");
                return false;
            }

            ts = dt - now;
        }
        else
        {
            foreach (var groupName in _regex.GetGroupNames())
            {
                if (groupName is "0" or "what")
                    continue;

                if (string.IsNullOrWhiteSpace(m.Groups[groupName].Value))
                {
                    values[groupName] = 0;
                    continue;
                }

                if (!int.TryParse(m.Groups[groupName].Value, out var value))
                {
                    Log.Warning("Reminder regex group {GroupName} has invalid value", groupName);
                    return false;
                }

                if (value < 1)
                {
                    Log.Warning("Reminder time value has to be an integer greater than 0");
                    return false;
                }

                values[groupName] = value;
            }

            ts = new TimeSpan((30 * values["mo"]) + (7 * values["w"]) + values["d"], values["h"], values["m"], 0);
        }


        obj = new()
        {
            Time = ts,
            What = what
        };

        return true;
    }

    private async Task ReminderTimerAction(Reminder r)
    {
        try
        {
            IMessageChannel ch;
            if (r.IsPrivate)
            {
                var user = (IUser)_client.GetUser(r.ChannelId)
                           ?? await _client.Rest.GetUserAsync(r.ChannelId);
                if (user is null)
                {
                    Log.Warning("Reminder {ReminderId} dropped because user {UserId} was not found", r.Id, r.ChannelId);
                    return;
                }
                ch = await user.CreateDMChannelAsync();
            }
            else
                ch = _client.GetGuild(r.GuildId)?.GetTextChannel(r.ChannelId);

            if (ch is null)
                return;

            var st = SmartText.CreateFrom(r.Message);

            var res = _sender.Response(ch)
                .UserBasedMentions(_client.GetGuild(r.GuildId)?.GetUser(r.UserId));

            if (st is SmartEmbedTextArray seta)
            {
                await res.Embeds(seta.GetEmbedBuilders()).SendAsync();
            }
            else
            {
                await res
                    .Embed(_sender.CreateEmbed(r.GuildId)
                        .WithOkColor()
                        .WithTitle("Reminder")
                        .AddField("Created At",
                            r.DateAdded.HasValue ? r.DateAdded.Value.ToLongDateString() : "?")
                        .AddField("By",
                            (await ch.GetUserAsync(r.UserId))?.ToString() ?? r.UserId.ToString()))
                    .Text(r.Message)
                    .SendAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error executing reminder {ReminderId}: {ErrorMessage}", r.Id, ex.Message);
        }
    }

    public struct RemindObject
    {
        public string What { get; set; }
        public TimeSpan Time { get; set; }
    }

    public async Task<bool> AddReminderAsync(
        ulong userId,
        ulong targetId,
        ulong? guildId,
        bool isPrivate,
        DateTime time,
        string message,
        ReminderType reminderType)
    {
        var gid = guildId ?? 0;
        var now = DateTime.UtcNow;
        int inserted;
        await using (var ctx = _db.GetDbContext())
        {
            if (reminderType != ReminderType.User)
            {
                inserted = await ctx.GetTable<Reminder>()
                    .InsertAsync(() => new Reminder
                    {
                        UserId = userId,
                        ChannelId = targetId,
                        GuildId = gid,
                        IsPrivate = isPrivate,
                        When = time,
                        Message = message,
                        Type = reminderType,
                        DateAdded = now
                    });
            }
            else
            {
                // The count and the insert are one statement, so parallel commands cannot pass the caps.
                // DM reminders all have server id 0, so the server cap does not apply to them.
                await using var lctx = ctx.CreateLinqToDBContext();
                var reminders = lctx.GetTable<Reminder>();
                inserted = await lctx.SelectQuery(() => 1)
                    .Where(_ => reminders.Count(r => r.UserId == userId && r.Type == ReminderType.User)
                                < MAX_REMINDERS_PER_USER
                                && (gid == 0
                                    || reminders.Count(r => r.GuildId == gid && r.Type == ReminderType.User)
                                    < MAX_REMINDERS_PER_SERVER))
                    .InsertAsync(reminders,
                        _ => new Reminder
                        {
                            UserId = userId,
                            ChannelId = targetId,
                            GuildId = gid,
                            IsPrivate = isPrivate,
                            When = time,
                            Message = message,
                            Type = ReminderType.User,
                            DateAdded = now
                        });
            }
        }

        if (inserted == 0)
            return false;

        _tcs?.TrySetResult(true);
        return true;
    }

    public async Task<List<Reminder>> GetServerReminders(int page, ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        return uow.Set<Reminder>().RemindersForServer(guildId, page).ToList();
    }

    public async Task<List<Reminder>> GetUserReminders(int page, ulong userId)
    {
        await using var uow = _db.GetDbContext();
        return uow.Set<Reminder>().RemindersFor(userId, page).ToList();
    }
}