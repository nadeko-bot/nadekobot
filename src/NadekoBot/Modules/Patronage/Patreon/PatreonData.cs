#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Patronage;

public sealed class Attributes
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; }

    [JsonPropertyName("is_follower")]
    public bool IsFollower { get; set; }

    [JsonPropertyName("last_charge_date")]
    public DateTime? LastChargeDate { get; set; }

    [JsonPropertyName("last_charge_status")]
    public string LastChargeStatus { get; set; }

    [JsonPropertyName("lifetime_support_cents")]
    public int LifetimeSupportCents { get; set; }

    [JsonPropertyName("currently_entitled_amount_cents")]
    public int CurrentlyEntitledAmountCents { get; set; }

    [JsonPropertyName("patron_status")]
    public string PatronStatus { get; set; }
}

public sealed class Data
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }
}

public sealed class Address
{
    [JsonPropertyName("data")]
    public Data Data { get; set; }
}

// public sealed class CurrentlyEntitledTiers
// {
//     [JsonPropertyName("data")]
//     public List<Datum> Data { get; set; }
// }

// public sealed class Relationships
// {
//     [JsonPropertyName("address")]
//     public Address Address { get; set; }
//
//     // [JsonPropertyName("currently_entitled_tiers")]
//     // public CurrentlyEntitledTiers CurrentlyEntitledTiers { get; set; }
// }

public sealed class PatreonMembersResponse
{
    [JsonPropertyName("data")]
    public List<PatreonMember> Data { get; set; }

    [JsonPropertyName("included")]
    public List<PatreonUser> Included { get; set; }

    [JsonPropertyName("links")]
    public PatreonLinks Links { get; set; }
}

public sealed class PatreonLinks
{
    [JsonPropertyName("next")]
    public string Next { get; set; }
}

public sealed class PatreonUser
{
    [JsonPropertyName("attributes")]
    public PatreonUserAttributes Attributes { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }
    // public string Type { get; set; }
}

public sealed class PatreonUserAttributes
{
    [JsonPropertyName("social_connections")]
    public PatreonSocials SocialConnections { get; set; }
}

public sealed class PatreonSocials
{
    [JsonPropertyName("discord")]
    public DiscordSocial Discord { get; set; }
}

public sealed class DiscordSocial
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; }
}

public sealed class PatreonMember
{
    [JsonPropertyName("attributes")]
    public Attributes Attributes { get; set; }

    [JsonPropertyName("relationships")]
    public Relationships Relationships { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }
}

public sealed class Relationships
{
    [JsonPropertyName("user")]
    public PatreonRelationshipUser User { get; set; }
}

public sealed class PatreonRelationshipUser
{
    [JsonPropertyName("data")]
    public PatreonUserData Data { get; set; }
}

public sealed class PatreonUserData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
}