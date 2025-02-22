#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Services.Database.Models;

public class WaifuInfo : DbEntity
{
    public int WaifuId { get; set; }
    public DiscordUser Waifu { get; set; }
    
    public int? ClaimerId { get; set; }
    public DiscordUser Claimer { get; set; }

    public int? AffinityId { get; set; }
    public DiscordUser Affinity { get; set; }

    public long Price { get; set; }
    public List<WaifuItem> Items { get; set; } = new();

    public override string ToString()
    {
        var status = string.Empty;

        var waifuUsername = Waifu.ToString().TrimTo(20);
        var claimer = Claimer?.ToString().TrimTo(20)
            ?? "no one";

        var affinity = Affinity?.ToString().TrimTo(20);

        if (AffinityId is null)
            status = $"... but {waifuUsername}'s heart is empty";
        else if (AffinityId == ClaimerId)
            status = $"... and {waifuUsername} likes {claimer} too <3";
        else
        {
            status =
                $"... but {waifuUsername}'s heart belongs to {affinity}";
        }

        return $"**{waifuUsername}** - claimed by **{claimer}**\n\t{status}";
    }
}

public class WaifuLbResult
{
    public string Username { get; set; }
    public string Discrim { get; set; }

    public string Claimer { get; set; }
    public string ClaimerDiscrim { get; set; }

    public string Affinity { get; set; }
    public string AffinityDiscrim { get; set; }

    public long Price { get; set; }

    public override string ToString()
    {
        var claimer = "no one";
        var status = string.Empty;

        var waifuUsername = Username.TrimTo(20);
        var claimerUsername = Claimer?.TrimTo(20);

        if (Claimer is not null)
            claimer = $"{claimerUsername}#{ClaimerDiscrim}";
        if (Affinity is null)
            status = $"... but {waifuUsername}'s heart is empty";
        else if (Affinity + AffinityDiscrim == Claimer + ClaimerDiscrim)
            status = $"... and {waifuUsername} likes {claimerUsername} too <3";
        else
            status = $"... but {waifuUsername}'s heart belongs to {Affinity.TrimTo(20)}#{AffinityDiscrim}";
        return $"**{waifuUsername}#{Discrim}** - claimed by **{claimer}**\n\t{status}";
    }
}