using System;
using System.Collections.Generic;
using System.Linq;
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using Discord;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;
using System.IO;
using System.Net;
using System.Net.Http;
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
    public void YouTubeAtomFeed_ParsesAndBuildsEmbedsWithThumbnails()
    {
        Assert.That(_feed.Title, Is.EqualTo("The Classical Music Channel"));
        Assert.That(_feed.Items, Has.Count.EqualTo(9));

        var first = _feed.Items.First();
        Assert.That(first.Title, Is.EqualTo("Johannes Brahms - Cello Sonata No. 1 in E Minor, Op. 38"));
        Assert.That(first.SpecificItem, Is.InstanceOf<AtomFeedItem>());
        Assert.That(first.PublishingDate, Is.Not.Null);

        var embed = FeedsService.BuildFeedEmbed(new EmbedBuilder(), first, RSS_URL);

        Assert.That(embed.Title, Is.EqualTo("Johannes Brahms - Cello Sonata No. 1 in E Minor, Op. 38"));
        Assert.That(embed.Url, Is.EqualTo("https://www.youtube.com/watch?v=-gmGalwAe5A"));
        Assert.That(embed.ImageUrl, Is.EqualTo("https://i2.ytimg.com/vi/-gmGalwAe5A/hqdefault.jpg"));
        Assert.That(embed.Footer?.Text, Is.EqualTo(RSS_URL));

        foreach (var item in _feed.Items)
        {
            var itemEmbed = FeedsService.BuildFeedEmbed(new EmbedBuilder(), item, RSS_URL);
            Assert.That(itemEmbed.ImageUrl, Is.Not.Null.And.Not.Empty,
                $"Missing thumbnail for: {item.Title}");
        }
    }

    [Test]
    public async Task ScanForChannelId_FindsCanonicalOrExternalId_AcrossChunkBoundary()
    {
        const string anchor =
            "<link rel=\"canonical\" href=\"https://www.youtube.com/channel/UC-lHJZR3Gqxm24_Vd_AJ5Yw\">";
        // Place the anchor so it starts a few bytes before the 64KB chunk boundary,
        // forcing the scanner to complete the match across two reads via the carry.
        var straddling = new string(' ', (64 * 1024) - 10) + anchor;

        (string Html, string Expected)[] cases =
        [
            ("<html><head><link rel=\"canonical\" "
             + "href=\"https://www.youtube.com/channel/UCX6OQ3DkcsbYNE6H8uQQuVA\"></head></html>",
                "UCX6OQ3DkcsbYNE6H8uQQuVA"),
            ("<html><body>{\"externalId\":\"UCXuqSBlHAE6Xw-yeJA0Tunw\"}</body></html>", "UCXuqSBlHAE6Xw-yeJA0Tunw"),
            (straddling, "UC-lHJZR3Gqxm24_Vd_AJ5Yw")
        ];

        foreach (var (html, expected) in cases)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));

            var id = await FeedsService.ScanForChannelIdAsync(stream, CancellationToken.None);

            Assert.That(id, Is.EqualTo(expected), expected);
        }
    }

    private static readonly DateTimeOffset _start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static FeedHealth CreateHealth(FakeTimeProvider time, params (string Key, string Url)[] feeds)
    {
        var health = new FeedHealth(time);
        Tick(health, feeds);
        return health;
    }

    private static void Tick(FeedHealth health, (string Key, string Url)[] feeds)
    {
        health.BeginTick();
        foreach (var (key, url) in feeds)
            health.Touch(key, url);
        health.EndTick();
    }

    [Test]
    public void PollInterval_FollowsCacheHeaders()
    {
        var now = _start;

        using var youtube = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        youtube.Headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromSeconds(900) };
        youtube.Headers.Age = TimeSpan.FromSeconds(328);

        using var plain = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };

        using var daily = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        daily.Headers.CacheControl = new() { MaxAge = TimeSpan.FromDays(1) };

        using var noStore = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        noStore.Headers.CacheControl = new() { NoStore = true, MaxAge = TimeSpan.FromMinutes(10) };

        Assert.That(FeedHealth.GetPollInterval(FeedHealth.GetFreshness(youtube, now)),
            Is.EqualTo(TimeSpan.FromSeconds(900 - 328 + 2)));
        Assert.That(FeedHealth.GetPollInterval(FeedHealth.GetFreshness(plain, now)),
            Is.EqualTo(FeedHealth.MinPollInterval));
        Assert.That(FeedHealth.GetPollInterval(FeedHealth.GetFreshness(daily, now)),
            Is.EqualTo(FeedHealth.MaxPollInterval));
        Assert.That(FeedHealth.GetPollInterval(FeedHealth.GetFreshness(noStore, now)),
            Is.EqualTo(FeedHealth.MinPollInterval));
    }

    [Test]
    public void FailingFeed_IsRemovedAfterDays_ButNotWhileItsHostIsDown()
    {
        var time = new FakeTimeProvider(_start);
        (string, string)[] feeds =
        [
            ("yt-ok", "https://www.youtube.com/feeds/videos.xml?channel_id=UCok"),
            ("yt-dead", "https://www.youtube.com/feeds/videos.xml?channel_id=UCdead"),
            ("solo", "https://blog.example.com/rss"),
            ("down-1", "https://down.example.com/a.xml"),
            ("down-2", "https://down.example.com/b.xml")
        ];
        var health = CreateHealth(time, feeds);

        FeedFailure dead = default, solo = default, down = default;
        for (var day = 0; day <= 14; day++)
        {
            Tick(health, feeds);
            health.OnSuccess("yt-ok", null);
            dead = health.OnFailure("yt-dead", 404, null);
            solo = health.OnFailure("solo", 500, null);
            down = health.OnFailure("down-1", 404, null);
            health.OnFailure("down-2", 404, null);

            if (day == 0)
                Assert.That(dead.StreakStarted && solo.StreakStarted && down.StreakStarted, Is.True);

            var streak = TimeSpan.FromDays(day);
            Assert.That(dead.Remove, Is.EqualTo(streak >= FeedHealth.RemoveAfter), $"day {day}");
            Assert.That(solo.Remove, Is.EqualTo(streak >= FeedHealth.RemoveAfter), $"day {day}");
            Assert.That(down.Remove, Is.EqualTo(streak >= FeedHealth.RemoveAfterHostDown), $"day {day}");

            time.Advance(TimeSpan.FromDays(1));
        }

        Assert.That(dead.StreakStarted, Is.False);
    }

    [Test]
    public void HostPause_FollowsRetryAfter_AndSuccessResetsIt()
    {
        var time = new FakeTimeProvider(_start);
        (string, string)[] feeds =
        [
            ("a", "https://www.youtube.com/feeds/videos.xml?channel_id=UCa"),
            ("b", "https://www.youtube.com/feeds/videos.xml?channel_id=UCb"),
            ("other", "https://other.example.com/rss")
        ];
        var health = CreateHealth(time, feeds);

        health.OnFailure("a", 429, TimeSpan.FromSeconds(120));
        Assert.That(health.IsDue("b"), Is.False);
        Assert.That(health.IsDue("other"), Is.True);

        time.Advance(TimeSpan.FromSeconds(121));
        Assert.That(health.IsDue("b"), Is.True);

        // The success resets the doubling, so the next pause without Retry-After is the first one again.
        health.OnSuccess("b", null);
        health.OnFailure("b", 403, null);
        time.Advance(FeedHealth.FirstHostPause - TimeSpan.FromMinutes(1));
        Assert.That(health.IsDue("a"), Is.False);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.That(health.IsDue("a"), Is.True);

        Assert.That(FeedHealth.GetHostPause(null, 0), Is.EqualTo(FeedHealth.FirstHostPause));
        Assert.That(FeedHealth.GetHostPause(null, 1), Is.EqualTo(FeedHealth.FirstHostPause * 2));
        Assert.That(FeedHealth.GetHostPause(null, 10), Is.EqualTo(FeedHealth.MaxHostPause));
        Assert.That(FeedHealth.GetHostPause(TimeSpan.FromSeconds(1), 3), Is.EqualTo(FeedHealth.MinHostPause));
        Assert.That(FeedHealth.GetHostPause(TimeSpan.FromDays(1), 0), Is.EqualTo(FeedHealth.MaxHostPause));
    }

    [Test]
    public void RestoredStreak_NeedsAnHourOfFailuresInThisRun()
    {
        var time = new FakeTimeProvider(_start);
        var health = new FeedHealth(time);
        health.BeginTick();
        health.Touch("solo", "https://blog.example.com/rss", _start.UtcDateTime.AddDays(-5));
        health.EndTick();

        Assert.That(health.OnFailure("solo", 500, null).Remove, Is.False);

        time.Advance(FeedHealth.MinObservedStreak);
        Assert.That(health.OnFailure("solo", 500, null).Remove, Is.True);
    }

    [Test]
    public void ForbiddenFeed_DoesNotBlockItsHost()
    {
        var time = new FakeTimeProvider(_start);
        (string, string)[] feeds =
        [
            ("bad", "https://www.reddit.com/r/private/.rss"),
            ("good", "https://www.reddit.com/r/public/.rss")
        ];
        var health = CreateHealth(time, feeds);

        health.OnFailure("bad", 403, null);
        Assert.That(health.IsDue("good"), Is.False);

        time.Advance(FeedHealth.FirstHostPause);
        Assert.That(health.IsDue("good"), Is.True);
        Assert.That(health.IsDue("bad"), Is.False);

        health.OnSuccess("good", null);
        time.Advance(FeedHealth.GetFailureBackoff(1));
        Assert.That(health.IsDue("bad"), Is.True);

        health.OnFailure("bad", 403, null);
        Assert.That(health.IsDue("good"), Is.True);
    }

    [Test]
    public async Task Streaks_AreSavedAndCleared_AndTheOldestStartLoadsBack()
    {
        using var db = new TestDbService();
        var oldStart = _start.UtcDateTime.AddDays(-1);
        var now = _start.UtcDateTime;

        await using (var ctx = db.GetDbContext())
        {
            await ctx.BulkCopyAsync(new[]
                {
                    new FeedSub { GuildId = 1, ChannelId = 1, Url = RSS_URL, FailingSince = oldStart },
                    new FeedSub { GuildId = 2, ChannelId = 2, Url = RSS_URL },
                    new FeedSub { GuildId = 3, ChannelId = 3, Url = "https://other.example.com/rss" }
                });
        }

        List<FeedSub> rows;
        await using (var ctx = db.GetDbContext())
            rows = await ctx.GetTable<FeedSub>().OrderBy(x => x.GuildId).ToListAsyncLinqToDB();

        var started = new HashSet<int> { rows[0].Id, rows[1].Id };
        await FeedsService.SaveStreaksAsync(db, started, [], now);

        await using (var ctx = db.GetDbContext())
            rows = await ctx.GetTable<FeedSub>().OrderBy(x => x.GuildId).ToListAsyncLinqToDB();

        Assert.That(started, Is.Empty);
        Assert.That(rows.Select(x => x.FailingSince), Is.EqualTo(new DateTime?[] { oldStart, now, null }));
        Assert.That(FeedsService.GetFailingSince(rows.Where(x => x.Url == RSS_URL).ToList()), Is.EqualTo(oldStart));

        var ended = new HashSet<int> { rows[0].Id, rows[1].Id };
        await FeedsService.SaveStreaksAsync(db, [], ended, now);

        await using (var ctx = db.GetDbContext())
            rows = await ctx.GetTable<FeedSub>().ToListAsyncLinqToDB();

        Assert.That(ended, Is.Empty);
        Assert.That(rows.All(x => x.FailingSince is null), Is.True);
    }
}
