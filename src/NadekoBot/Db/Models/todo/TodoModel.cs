namespace NadekoBot.Db.Models;

#nullable disable
public sealed class TodoModel
{
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public string Todo { get; set; }

    public DateTime DateAdded { get; set; }
    public bool IsDone { get; set; }
    public int? ArchiveId { get; set; }
}