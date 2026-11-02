namespace NadekoBot.Db.Models;

#nullable disable
[ShardFiltered]
public sealed class GiveawayModel
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong MessageId { get; set; }
    public ulong ChannelId { get; set; }
    public string Message { get; set; }

    public IList<GiveawayUser> Participants { get; set; } = new List<GiveawayUser>();
    public DateTime EndsAt { get; set; }
    /// <summary>
    /// How many members win when the giveaway ends, 1 to 20.
    /// </summary>
    public int WinnerCount { get; set; } = 1;
}