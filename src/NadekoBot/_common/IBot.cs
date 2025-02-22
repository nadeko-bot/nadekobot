#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot;

public interface IBot
{
    bool IsReady { get; }
}