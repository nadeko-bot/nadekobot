#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class PicartoChannelResponse
{
    [JsonProperty("user_id")]
    public int UserId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("avatar")]
    public string Avatar { get; set; }

    [JsonProperty("online")]
    public bool Online { get; set; }

    [JsonProperty("viewers")]
    public int Viewers { get; set; }

    [JsonProperty("viewers_total")]
    public int ViewersTotal { get; set; }

    [JsonProperty("thumbnails")]
    public Thumbnails Thumbnails { get; set; }

    [JsonProperty("followers")]
    public int Followers { get; set; }

    [JsonProperty("subscribers")]
    public int Subscribers { get; set; }

    [JsonProperty("adult")]
    public bool Adult { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("account_type")]
    public string AccountType { get; set; }

    [JsonProperty("commissions")]
    public bool Commissions { get; set; }

    [JsonProperty("recordings")]
    public bool Recordings { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description_panels")]
    public List<DescriptionPanel> DescriptionPanels { get; set; }

    [JsonProperty("private")]
    public bool Private { get; set; }

    [JsonProperty("private_message")]
    public string PrivateMessage { get; set; }

    [JsonProperty("gaming")]
    public bool Gaming { get; set; }

    [JsonProperty("chat_settings")]
    public ChatSettings ChatSettings { get; set; }

    [JsonProperty("last_live")]
    public DateTime LastLive { get; set; }

    [JsonProperty("tags")]
    public List<string> Tags { get; set; }

    [JsonProperty("multistream")]
    public List<Multistream> Multistream { get; set; }

    [JsonProperty("languages")]
    public List<Language> Languages { get; set; }

    [JsonProperty("following")]
    public bool Following { get; set; }
}

public class Thumbnails
{
    [JsonProperty("web")]
    public string Web { get; set; }

    [JsonProperty("web_large")]
    public string WebLarge { get; set; }

    [JsonProperty("mobile")]
    public string Mobile { get; set; }

    [JsonProperty("tablet")]
    public string Tablet { get; set; }
}

public class DescriptionPanel
{
    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("body")]
    public string Body { get; set; }

    [JsonProperty("image")]
    public string Image { get; set; }

    [JsonProperty("image_link")]
    public string ImageLink { get; set; }

    [JsonProperty("button_text")]
    public string ButtonText { get; set; }

    [JsonProperty("button_link")]
    public string ButtonLink { get; set; }

    [JsonProperty("position")]
    public int Position { get; set; }
}

public class ChatSettings
{
    [JsonProperty("guest_chat")]
    public bool GuestChat { get; set; }

    [JsonProperty("links")]
    public bool Links { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }
}

public class Multistream
{
    [JsonProperty("user_id")]
    public int UserId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("online")]
    public bool Online { get; set; }

    [JsonProperty("adult")]
    public bool Adult { get; set; }
}

public class Language
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}