using NadekoBot.Db.Models;

namespace Nadeko.Common;

public static class Extensions
{
    public static long ToTimestamp(this in DateTime value)
        => (value.Ticks - 621355968000000000) / 10000000;
}