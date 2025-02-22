#nullable disable
namespace NadekoBot.Modules.Searches.Common;

public class WikipediaApiModel
{
    public WikipediaQuery Query { get; set; }

    public class WikipediaQuery
    {
        public WikipediaPage[] Pages { get; set; }

        public class WikipediaPage
        {
            public bool Missing { get; set; } = false;
            public string FullUrl { get; set; }
        }
    }
}