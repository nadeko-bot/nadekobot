using System.Collections.Frozen;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public sealed class GuildIgnores
{
    public FrozenSet<ulong> Users { get; }
    public FrozenSet<ulong> Channels { get; }
    public FrozenSet<ulong> Categories { get; }
    public IReadOnlyList<LogIgnore> All { get; }

    public GuildIgnores(IReadOnlyList<LogIgnore> all)
    {
        All = all;

        var users = new HashSet<ulong>();
        var chans = new HashSet<ulong>();
        var cats = new HashSet<ulong>();

        foreach (var i in all)
        {
            switch (i.ItemType)
            {
                case IgnoredItemType.User:
                    users.Add(i.LogItemId);
                    break;
                case IgnoredItemType.Channel:
                    chans.Add(i.LogItemId);
                    break;
                case IgnoredItemType.Category:
                    cats.Add(i.LogItemId);
                    break;
            }
        }

        Users = users.ToFrozenSet();
        Channels = chans.ToFrozenSet();
        Categories = cats.ToFrozenSet();
    }
}
