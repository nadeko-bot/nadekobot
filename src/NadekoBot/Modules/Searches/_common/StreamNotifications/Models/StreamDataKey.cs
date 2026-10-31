#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Searches.Common;

// Platform names are ASCII and the platforms ignore their case, so a key matches in any letter case.
public readonly record struct StreamDataKey
{
    public FollowedStream.FType Type { get; init; }
    public string Name { get; init; }

    public StreamDataKey(FollowedStream.FType type, string name)
    {
        Type = type;
        Name = name;
    }

    public bool Equals(StreamDataKey other)
        => Type == other.Type && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => HashCode.Combine(Type, Name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Name));
}
