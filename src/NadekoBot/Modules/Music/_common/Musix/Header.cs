using System.Text.Json.Serialization;

namespace Musix.Models;

public class Header
{
    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("execute_time")]
    public double ExecuteTime { get; set; }
}