using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

/// <summary>
/// A server where the bot hides the moderator of a warning from the warned user.
/// </summary>
public sealed class HiddenWarnModGuild
{
    /// <summary>
    /// Id of the server.
    /// </summary>
    public ulong GuildId { get; set; }
}

public sealed class HiddenWarnModGuildEntityConfiguration : IEntityTypeConfiguration<HiddenWarnModGuild>
{
    public void Configure(EntityTypeBuilder<HiddenWarnModGuild> builder)
    {
        builder.HasKey(x => x.GuildId);
        builder.Property(x => x.GuildId).ValueGeneratedNever();
    }
}
