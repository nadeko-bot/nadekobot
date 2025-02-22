using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Games;

public sealed class FishCatch

{
    [Key]
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public int FishId { get; set; }
    public int Count { get; set; }
    public int MaxStars { get; set; }
}

public sealed class FishCatchConfiguration : IEntityTypeConfiguration<FishCatch>
{
    public void Configure(EntityTypeBuilder<FishCatch> builder)
    {
        builder.HasAlternateKey(x => new
        {
            x.UserId,
            x.FishId
        });
    }
}