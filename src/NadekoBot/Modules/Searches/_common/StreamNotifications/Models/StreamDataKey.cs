#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Searches.Common;

public readonly record struct StreamDataKey
{
    public FollowedStream.FType Type { get; init; }
    public string Name { get; init; }

    public StreamDataKey(FollowedStream.FType type, string name)
    {
        Type = type;
        Name = name;
    }
}