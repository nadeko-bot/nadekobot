using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

public class DelMsgOnCmdChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public bool State { get; set; }
}

public class DelMsgOnCmdChannelEntityConfiguration : IEntityTypeConfiguration<DelMsgOnCmdChannel>
{
    public void Configure(EntityTypeBuilder<DelMsgOnCmdChannel> builder)
    {
        builder.HasIndex(x => new
        {
            x.GuildId,
            x.ChannelId
        }).IsUnique();
    }
}
