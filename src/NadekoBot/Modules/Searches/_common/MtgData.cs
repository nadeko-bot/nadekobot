#nullable disable
namespace NadekoBot.Modules.Searches.Common;

public class MtgData
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageUrl { get; set; }
    public string StoreUrl { get; set; }
    public string Types { get; set; }
    public string ManaCost { get; set; }
}

public class MtgResponse
{
    public List<Data> Cards { get; set; }

    public class Data
    {
        public string Name { get; set; }
        public string ManaCost { get; set; }
        public string Text { get; set; }
        public List<string> Types { get; set; }
        public string ImageUrl { get; set; }
    }
}