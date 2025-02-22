#nullable disable
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class Quote : DbEntity
{
    public ulong GuildId { get; set; }

    [Required]
    public string Keyword { get; set; }

    [Required]
    public string AuthorName { get; set; }

    public ulong AuthorId { get; set; }

    [Required]
    public string Text { get; set; }
}

public enum OrderType
{
    Id = -1,
    Keyword = -2
}