#nullable disable
namespace NadekoBot.Modules.Searches.Common;

public sealed class Tag
{
    public string Name { get; set; }
    public string Url { get; set; }
}

public sealed class Gallery
{
    public uint Id { get; }
    public string Url { get; }
    public string FullTitle { get; }
    public string Title { get; }
    public string Thumbnail { get; }
    public int PageCount { get; }
    public int Likes { get; }
    public DateTime UploadedAt { get; }
    public Tag[] Tags { get; }


    public Gallery(
        uint id,
        string url,
        string fullTitle,
        string title,
        string thumbnail,
        int pageCount,
        int likes,
        DateTime uploadedAt,
        Tag[] tags)
    {
        Id = id;
        Url = url;
        FullTitle = fullTitle;
        Title = title;
        Thumbnail = thumbnail;
        PageCount = pageCount;
        Likes = likes;
        UploadedAt = uploadedAt;
        Tags = tags;
    }
}