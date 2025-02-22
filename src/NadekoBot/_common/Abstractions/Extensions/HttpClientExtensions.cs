using System.Net.Http.Headers;

namespace Nadeko.Common;

public static class HttpClientExtensions
{
    public static HttpClient AddFakeHeaders(this HttpClient http)
    {
        AddFakeHeaders(http.DefaultRequestHeaders);
        return http;
    }

    public static void AddFakeHeaders(this HttpHeaders dict)
    {
        dict.Clear();
        dict.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        dict.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/535.1 (KHTML, like Gecko) Chrome/14.0.835.202 Safari/535.1");
    }

    public static bool IsImage(this HttpResponseMessage msg)
        => IsImage(msg, out _);

    public static bool IsImage(this HttpResponseMessage msg, out string? mimeType)
    {
        mimeType = msg.Content.Headers.ContentType?.MediaType;
        if (mimeType is "image/png" or "image/jpeg" or "image/gif")
            return true;

        return false;
    }

    public static long GetContentLength(this HttpResponseMessage msg)
        => msg.Content.Headers.ContentLength ?? long.MaxValue;
}