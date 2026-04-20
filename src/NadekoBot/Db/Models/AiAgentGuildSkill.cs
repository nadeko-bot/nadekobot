using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public class AiAgentGuildSkill
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong? ChannelId { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(2000)]
    public string Instruction { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;
}

public sealed class AiAgentGuildSkillEntityConfiguration : IEntityTypeConfiguration<AiAgentGuildSkill>
{
    public void Configure(EntityTypeBuilder<AiAgentGuildSkill> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.ChannelId, x.Name }).IsUnique();
        builder.HasIndex(x => x.GuildId);
    }
}
