#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.Services;

public interface IRemindService
{
    Task<bool> AddReminderAsync(ulong userId,
        ulong targetId,
        ulong? guildId,
        bool isPrivate,
        DateTime time,
        string message,
        ReminderType reminderType);
}