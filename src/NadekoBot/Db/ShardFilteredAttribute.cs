namespace NadekoBot.Db;

/// <summary>
/// Marks an entity as shard-filtered via its GuildId property.
/// NadekoDbService will auto-create expression indexes on the table
/// for shard-scoped startup queries.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ShardFilteredAttribute : Attribute;
