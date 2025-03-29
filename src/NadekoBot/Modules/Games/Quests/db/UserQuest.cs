using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Modules.Games.Quests;

namespace NadekoBot.Db.Models;

public class UserQuest
{
    [Key]
    public int Id { get; set; }

    public int QuestNumber { get; set; }
    public ulong UserId { get; set; }

    public QuestIds QuestId { get; set; }

    public long Progress { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime DateAssigned { get; set; }
}

public sealed class UserQuestEntityConfiguration : IEntityTypeConfiguration<UserQuest>
{
    public void Configure(EntityTypeBuilder<UserQuest> builder)
    {
        builder.HasIndex(x => x.UserId);
        
        builder.HasIndex(x => new
        {
            x.UserId,
            x.QuestNumber,
            x.DateAssigned
        }).IsUnique();
    }
}