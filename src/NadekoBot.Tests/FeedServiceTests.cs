using System.Linq;
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using Discord;
using NadekoBot.Modules.Searches.Services;
using NUnit.Framework;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NadekoBot.Tests;

public class FeedServiceTests
{
    private const string YOUTUBE_ATOM_FEED = """
<?xml version="1.0" encoding="UTF-8"?>
<feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">
 <link rel="self" href="http://www.youtube.com/feeds/videos.xml?channel_id=UCwsUEImuhcqjgOuHAIJKL_g"/>
 <id>yt:channel:wsUEImuhcqjgOuHAIJKL_g</id>
 <yt:channelId>wsUEImuhcqjgOuHAIJKL_g</yt:channelId>
 <title>The Classical Music Channel</title>
 <link rel="alternate" href="https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g"/>
 <author>
  <name>The Classical Music Channel</name>
  <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
 </author>
 <published>2014-01-12T06:02:11+00:00</published>
 <entry>
  <id>yt:video:-gmGalwAe5A</id>
  <yt:videoId>-gmGalwAe5A</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Johannes Brahms - Cello Sonata No. 1 in E Minor, Op. 38</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=-gmGalwAe5A"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2016-01-24T02:07:38+00:00</published>
  <updated>2025-06-05T20:12:04+00:00</updated>
  <media:group>
   <media:title>Johannes Brahms - Cello Sonata No. 1 in E Minor, Op. 38</media:title>
   <media:content url="https://www.youtube.com/v/-gmGalwAe5A?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i2.ytimg.com/vi/-gmGalwAe5A/hqdefault.jpg" width="480" height="360"/>
   <media:description>Brahms Cello Sonata No. 1 in E Minor
Cello: Yo-yo Ma
Piano: Emanuel Ax
I. Allegro non troppo: 00:00
II. Allegretto quasi Menuetto: 14:46
III. Allegro: 20:50</media:description>
   <media:community>
    <media:starRating count="17" average="5.00" min="1" max="5"/>
    <media:statistics views="1599"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:CEvg8d16Cf0</id>
  <yt:videoId>CEvg8d16Cf0</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Dimitri Shostakovich - Cello Concerto No. 2 in G Minor, Op. 126</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=CEvg8d16Cf0"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2015-08-06T14:46:36+00:00</published>
  <updated>2025-06-18T06:11:48+00:00</updated>
  <media:group>
   <media:title>Dimitri Shostakovich - Cello Concerto No. 2 in G Minor, Op. 126</media:title>
   <media:content url="https://www.youtube.com/v/CEvg8d16Cf0?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i4.ytimg.com/vi/CEvg8d16Cf0/hqdefault.jpg" width="480" height="360"/>
   <media:description>Dimitri Shostakovich's Cello Concerto No. 2 in G Minor
Soloist: Pieter Wispelwey
Sinfonietta Cracovia
Conductor: Jurjen Hempel
I. Largo: 00:00
II. Allegretto: 14:57
III. Allegretto: Approx. 19:38</media:description>
   <media:community>
    <media:starRating count="27" average="5.00" min="1" max="5"/>
    <media:statistics views="2850"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:IQEnqJVd_Q0</id>
  <yt:videoId>IQEnqJVd_Q0</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.6 in D Major, BWV 1012 (HD)</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=IQEnqJVd_Q0"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2015-04-19T12:48:38+00:00</published>
  <updated>2025-06-12T03:55:47+00:00</updated>
  <media:group>
   <media:title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.6 in D Major, BWV 1012 (HD)</media:title>
   <media:content url="https://www.youtube.com/v/IQEnqJVd_Q0?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i2.ytimg.com/vi/IQEnqJVd_Q0/hqdefault.jpg" width="480" height="360"/>
   <media:description>Maurice Gendron plays J.S Bach's Cello Suite No.5 in C Minor, BWV 1011
I. Prelude 0:00
II. Allemande 5:07
III. Courante 13:21
IV. Sarabande 17:12
V. Gavotte I / Gavotte II 21:43
VI. Gigue 25:39</media:description>
   <media:community>
    <media:starRating count="132" average="5.00" min="1" max="5"/>
    <media:statistics views="10916"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:qkq0FHFDu3U</id>
  <yt:videoId>qkq0FHFDu3U</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.5 in C Minor, BWV 1011 (HD)</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=qkq0FHFDu3U"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2015-04-10T04:47:38+00:00</published>
  <updated>2025-06-06T02:58:32+00:00</updated>
  <media:group>
   <media:title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.5 in C Minor, BWV 1011 (HD)</media:title>
   <media:content url="https://www.youtube.com/v/qkq0FHFDu3U?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i2.ytimg.com/vi/qkq0FHFDu3U/hqdefault.jpg" width="480" height="360"/>
   <media:description>Maurice Gendron plays J.S Bach's Cello Suite No.5 in C Minor, BWV 1011
I. Prelude 0:00
II. Allemande 5:47
III. Courante 11:11
IV. Sarabande 13:41
V. Gavotte I / Gavotte II 17:09
VI. Gigue 21:56</media:description>
   <media:community>
    <media:starRating count="83" average="5.00" min="1" max="5"/>
    <media:statistics views="6316"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:sveYmbRNfGg</id>
  <yt:videoId>sveYmbRNfGg</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.4 in E flat major, BWV 1010 (HD)</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=sveYmbRNfGg"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2015-04-07T14:59:12+00:00</published>
  <updated>2025-06-11T17:49:48+00:00</updated>
  <media:group>
   <media:title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.4 in E flat major, BWV 1010 (HD)</media:title>
   <media:content url="https://www.youtube.com/v/sveYmbRNfGg?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i4.ytimg.com/vi/sveYmbRNfGg/hqdefault.jpg" width="480" height="360"/>
   <media:description>Maurice Gendron plays J.S Bach's Cello Suite No.4 in E flat Major, BWV 1010
I. Prelude 0:00
II. Allemande 5:08
III. Courante 8:26
IV. Sarabande 12:29
V. Bourree I-II 17:17
VI. Gigue 22:40</media:description>
   <media:community>
    <media:starRating count="90" average="5.00" min="1" max="5"/>
    <media:statistics views="7964"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:htOYtjgvw40</id>
  <yt:videoId>htOYtjgvw40</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.3 in C major, BWV 1009 (HD)</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=htOYtjgvw40"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2015-04-06T11:17:44+00:00</published>
  <updated>2025-06-07T12:42:24+00:00</updated>
  <media:group>
   <media:title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.3 in C major, BWV 1009 (HD)</media:title>
   <media:content url="https://www.youtube.com/v/htOYtjgvw40?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i1.ytimg.com/vi/htOYtjgvw40/hqdefault.jpg" width="480" height="360"/>
   <media:description>Maurice Gendron plays J.S Bach's Cello Suite No.3 in C Major, BWV 1009
I. Prelude 0:00
II. Allemande 3:36
III. Courante 7:10
IV. Sarabande 10:23
V. Bourree I-II 14:47
VI. Gigue 17:56</media:description>
   <media:community>
    <media:starRating count="150" average="5.00" min="1" max="5"/>
    <media:statistics views="12595"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:Odqhtlri5E8</id>
  <yt:videoId>Odqhtlri5E8</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Mozart Horn Concerto in D major</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=Odqhtlri5E8"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2014-08-11T04:07:09+00:00</published>
  <updated>2025-06-15T02:00:48+00:00</updated>
  <media:group>
   <media:title>Mozart Horn Concerto in D major</media:title>
   <media:content url="https://www.youtube.com/v/Odqhtlri5E8?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i4.ytimg.com/vi/Odqhtlri5E8/hqdefault.jpg" width="480" height="360"/>
   <media:description>Mozart's Horn Concerto in D major, K386 (412/514)
Played by the St Martin Philharmonic Orchestra
Conductor: Neville Marriner
Soloist: Alan Civil
I. Allegro 00:08
II. Rondo (Allegro) 04:44</media:description>
   <media:community>
    <media:starRating count="15" average="5.00" min="1" max="5"/>
    <media:statistics views="783"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:fhvwZd5Hmyg</id>
  <yt:videoId>fhvwZd5Hmyg</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.2 in D Minor, BWV 1008 (HD)</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=fhvwZd5Hmyg"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2014-02-16T05:01:31+00:00</published>
  <updated>2025-11-30T14:59:29+00:00</updated>
  <media:group>
   <media:title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.2 in D Minor, BWV 1008 (HD)</media:title>
   <media:content url="https://www.youtube.com/v/fhvwZd5Hmyg?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i3.ytimg.com/vi/fhvwZd5Hmyg/hqdefault.jpg" width="480" height="360"/>
   <media:description>Maurice Gendron plays J.S Bach's Cello Suite No.2 in D Minor, BWV 1008
I. Prelude 00:05
II. Allemande 03:37
III. Courante 07:15
IV. Sarabande 09:15
V. Menuet I-II 13:51
VI. Gigue 16:51</media:description>
   <media:community>
    <media:starRating count="461" average="5.00" min="1" max="5"/>
    <media:statistics views="55217"/>
   </media:community>
  </media:group>
 </entry>
 <entry>
  <id>yt:video:TbugsVd10gA</id>
  <yt:videoId>TbugsVd10gA</yt:videoId>
  <yt:channelId>UCwsUEImuhcqjgOuHAIJKL_g</yt:channelId>
  <title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.1 in G major, BWV 1007 (HD)</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=TbugsVd10gA"/>
  <author>
   <name>The Classical Music Channel</name>
   <uri>https://www.youtube.com/channel/UCwsUEImuhcqjgOuHAIJKL_g</uri>
  </author>
  <published>2014-01-12T07:09:50+00:00</published>
  <updated>2026-01-24T07:23:42+00:00</updated>
  <media:group>
   <media:title>Maurice Gendron - J.S. Bach Suite for Cello Solo No.1 in G major, BWV 1007 (HD)</media:title>
   <media:content url="https://www.youtube.com/v/TbugsVd10gA?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
   <media:thumbnail url="https://i1.ytimg.com/vi/TbugsVd10gA/hqdefault.jpg" width="480" height="360"/>
   <media:description>Maurice Gendron plays J.S Bach's Cello Suite No.1 in G major, BWV 1007
I. Prelude 00:00
II. Allemande 02:26
III. Courante 06:34
IV. Sarabande 09:09
V. Menuet I-II 12:13
VI. Gigue 15:19</media:description>
   <media:community>
    <media:starRating count="816" average="5.00" min="1" max="5"/>
    <media:statistics views="64838"/>
   </media:community>
  </media:group>
 </entry>
</feed>
""";

    private const string RSS_URL = "https://www.youtube.com/feeds/videos.xml?channel_id=UCwsUEImuhcqjgOuHAIJKL_g";

    private Feed _feed;

    [SetUp]
    public void SetUp()
    {
        _feed = FeedReader.ReadFromString(YOUTUBE_ATOM_FEED);
    }

    [Test]
    public void YouTubeAtomFeed_ParsesCorrectly()
    {
        Assert.That(_feed.Title, Is.EqualTo("The Classical Music Channel"));
        Assert.That(_feed.Items, Has.Count.EqualTo(9));

        var first = _feed.Items.First();
        Assert.That(first.Title, Is.EqualTo("Johannes Brahms - Cello Sonata No. 1 in E Minor, Op. 38"));
        Assert.That(first.SpecificItem, Is.InstanceOf<AtomFeedItem>());
        Assert.That(first.PublishingDate, Is.Not.Null);
    }

    [Test]
    public void BuildFeedEmbed_YouTubeAtomFeed_ExtractsAllFields()
    {
        var first = _feed.Items.First();
        var embed = FeedsService.BuildFeedEmbed(new EmbedBuilder(), first, RSS_URL);

        Assert.That(embed.Title, Is.EqualTo("Johannes Brahms - Cello Sonata No. 1 in E Minor, Op. 38"));
        Assert.That(embed.Url, Is.EqualTo("https://www.youtube.com/watch?v=-gmGalwAe5A"));
        Assert.That(embed.ImageUrl, Is.EqualTo("https://i2.ytimg.com/vi/-gmGalwAe5A/hqdefault.jpg"));
        Assert.That(embed.Footer?.Text, Is.EqualTo(RSS_URL));
    }

    [Test]
    public void BuildFeedEmbed_AllEntries_HaveThumbnails()
    {
        foreach (var item in _feed.Items)
        {
            var embed = FeedsService.BuildFeedEmbed(new EmbedBuilder(), item, RSS_URL);
            Assert.That(embed.ImageUrl, Is.Not.Null.And.Not.Empty,
                $"Missing thumbnail for: {item.Title}");
        }
    }

    [Test]
    public async Task ScanForChannelId_CanonicalLinkAnchor_ReturnsId()
    {
        const string html =
            "<html><head><link rel=\"canonical\" "
            + "href=\"https://www.youtube.com/channel/UCX6OQ3DkcsbYNE6H8uQQuVA\"></head></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var id = await FeedsService.ScanForChannelIdAsync(stream, CancellationToken.None);

        Assert.That(id, Is.EqualTo("UCX6OQ3DkcsbYNE6H8uQQuVA"));
    }

    [Test]
    public async Task ScanForChannelId_ExternalIdAnchorOnly_ReturnsId()
    {
        const string html = "<html><body>{\"externalId\":\"UCXuqSBlHAE6Xw-yeJA0Tunw\"}</body></html>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var id = await FeedsService.ScanForChannelIdAsync(stream, CancellationToken.None);

        Assert.That(id, Is.EqualTo("UCXuqSBlHAE6Xw-yeJA0Tunw"));
    }

    [Test]
    public async Task ScanForChannelId_AnchorStraddlesChunkBoundary_ReturnsId()
    {
        const string anchor =
            "<link rel=\"canonical\" href=\"https://www.youtube.com/channel/UC-lHJZR3Gqxm24_Vd_AJ5Yw\">";
        // Place the anchor so it starts a few bytes before the 64KB chunk boundary,
        // forcing the scanner to complete the match across two reads via the carry.
        var padLength = (64 * 1024) - 10;
        var sb = new StringBuilder(padLength + anchor.Length);
        sb.Append(' ', padLength);
        sb.Append(anchor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        var id = await FeedsService.ScanForChannelIdAsync(stream, CancellationToken.None);

        Assert.That(id, Is.EqualTo("UC-lHJZR3Gqxm24_Vd_AJ5Yw"));
    }
}
