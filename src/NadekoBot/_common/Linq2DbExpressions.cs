using System.Linq.Expressions;

namespace NadekoBot.Common;

public static class Queries
{
    private const ulong SHARD_DIVISOR = 4194304;

    /// <summary>
    /// Builds a WHERE predicate that filters rows to the given shard.
    /// totalShards and shardId are inlined as SQL literals (not bind parameters)
    /// so that SQLite's query planner can match expression indexes on
    /// (GuildId / 4194304 % N).
    /// </summary>
    public static Expression<Func<T, bool>> GuildOnShard<T>(
        Expression<Func<T, ulong>> guildIdSelector,
        int totalShards,
        int shardId)
    {
        var param = guildIdSelector.Parameters[0];
        var guildId = guildIdSelector.Body;

        // guildId / 4194304 % <totalShards> == <shardId>
        var expr = Expression.Equal(
            Expression.Modulo(
                Expression.Divide(guildId, Expression.Constant(SHARD_DIVISOR)),
                Expression.Constant((ulong)totalShards)),
            Expression.Constant((ulong)shardId));

        return Expression.Lambda<Func<T, bool>>(expr, param);
    }
}