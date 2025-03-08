using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Musix.Models
{
    public class MusixMatchResponse<T>
    {
        [JsonPropertyName("message")]
        public Message<T> Message { get; set; } = null!;
    }
}
