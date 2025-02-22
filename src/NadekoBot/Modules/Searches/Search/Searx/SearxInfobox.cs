// using System.Text.Json.Serialization;
//
// namespace NadekoBot.Modules.Searches;
//
// public sealed class SearxInfobox
// {
//     [JsonPropertyName("infobox")]
//     public string Infobox { get; set; }
//
//     [JsonPropertyName("id")]
//     public string Id { get; set; }
//
//     [JsonPropertyName("content")]
//     public string Content { get; set; }
//
//     [JsonPropertyName("img_src")]
//     public string ImgSrc { get; set; }
//
//     [JsonPropertyName("urls")]
//     public List<SearxUrlData> Urls { get; } = new List<SearxUrlData>();
//
//     [JsonPropertyName("engine")]
//     public string Engine { get; set; }
//
//     [JsonPropertyName("engines")]
//     public List<string> Engines { get; } = new List<string>();
//
//     [JsonPropertyName("attributes")]
//     public List<SearxSearchAttribute> Attributes { get; } = new List<SearxSearchAttribute>();
// }