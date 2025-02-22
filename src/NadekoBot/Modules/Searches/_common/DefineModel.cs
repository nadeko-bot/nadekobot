#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class Audio
{
    public string Url { get; set; }
}

public class Example
{
    public List<Audio> Audio { get; set; }
    public string Text { get; set; }
}

public class GramaticalInfo
{
    public string Type { get; set; }
}

public class Sens
{
    public object Definition { get; set; }
    public List<Example> Examples { get; set; }

    [JsonProperty("gramatical_info")]
    public GramaticalInfo GramaticalInfo { get; set; }
}

public class Result
{
    [JsonProperty("part_of_speech")]
    public string PartOfSpeech { get; set; }

    public List<Sens> Senses { get; set; }
    public string Url { get; set; }
}

public class DefineModel
{
    public List<Result> Results { get; set; }
}