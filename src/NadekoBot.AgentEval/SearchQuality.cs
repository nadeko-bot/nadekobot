using System.Diagnostics;

namespace NadekoBot.AgentEval;

// Expected holds the first alias of each command which answers the query.
public sealed record SearchCase(string Query, string[] Expected);

public sealed record SearchMiss(string Query, string[] Expected, string[] Got);

public sealed record SearchReport(int Cases, int Top1, int Top5, double AvgMs, IReadOnlyList<SearchMiss> Misses, IReadOnlyList<string> UnknownLabels);

public static class SearchQuality
{
    public static IReadOnlyList<SearchCase> Cases { get; } =
    [
        new("what does serendipity mean", ["define"]),
        new("define the word ephemeral", ["define"]),
        new("urban dictionary meaning of yeet", ["urbandict"]),
        new("flip a coin", ["flip"]),
        new("roll a dice", ["roll"]),
        new("roll 2d20", ["roll"]),
        new("how much money do i have", ["cash"]),
        new("check my balance", ["cash", "bank balance"]),
        new("give 100 currency to @bob", ["give"]),
        new("remind me in 10 minutes to drink water", ["remind"]),
        new("show my reminders", ["remindlist"]),
        new("ban @spammer", ["ban"]),
        new("kick that user", ["kick"]),
        new("mute @user for 10 minutes", ["mute", "timeout", "chatmute"]),
        new("timeout @user for an hour", ["timeout"]),
        new("warn @user for being rude", ["warn"]),
        new("show the warnings of @user", ["warnlog"]),
        new("delete the last 20 messages", ["prune"]),
        new("play despacito", ["play"]),
        new("skip this song", ["next"]),
        new("pause the music", ["pause"]),
        new("what song is playing", ["nowplaying"]),
        new("show the music queue", ["listqueue"]),
        new("stop the music and leave the voice channel", ["destroy", "stop"]),
        new("set the volume to 50", ["volume"]),
        new("shuffle the queue", ["queueshuffle"]),
        new("show the lyrics of this song", ["lyrics"]),
        new("what time is it in tokyo", ["time"]),
        new("weather in london", ["weather"]),
        new("translate hello to spanish", ["translate"]),
        new("show my xp", ["experience"]),
        new("xp leaderboard", ["xpleaderboard"]),
        new("who is the richest on the server", ["leaderboard"]),
        new("ask the magic 8 ball if i will pass my exam", ["eightball"]),
        new("play rock paper scissors", ["rps"]),
        new("claim my daily reward", ["timely"]),
        new("apple stock price", ["stock"]),
        new("bitcoin price", ["crypto"]),
        new("search youtube for lofi music", ["youtube"]),
        new("google how tall is the eiffel tower", ["google"]),
        new("show my avatar", ["avatar"]),
        new("show a random cat", ["randomcat"]),
        new("random dog picture", ["randomdog"]),
        new("tell me a joke", ["randjoke"]),
        new("chuck norris joke", ["chucknorris"]),
        new("look up pikachu", ["pokemon"]),
        new("find the anime naruto", ["anime"]),
        new("search wikipedia for black holes", ["wiki"]),
        new("convert 10 km to miles", ["convert"]),
        new("calculate 15% of 230", ["calculate"]),
        new("who has the moderator role", ["inrole"]),
        new("server info", ["serverinfo"]),
        new("info about @user", ["userinfo"]),
        new("create a role called VIP", ["createrole"]),
        new("give @user the member role", ["setrole"]),
        new("remove the muted role from @user", ["removerole"]),
        new("change the color of the vip role to red", ["rolecolor"]),
        new("set slowmode to 10 seconds", ["slowmode"]),
        new("lock this channel", ["lock"]),
        new("create a text channel called memes", ["creatxtchanl"]),
        new("set the channel topic", ["settopic"]),
        new("add a quote", ["quoteadd"]),
        new("show a random quote with the keyword hello", ["quoteprint", "quotesearch"]),
        new("start a giveaway", ["ga start"]),
        new("start a trivia game", ["trivia"]),
        new("play hangman", ["hangman"]),
        new("play blackjack for 100", ["blackjack"]),
        new("bet 50 on heads", ["betflip"]),
        new("spin the slot machine", ["slot"]),
        new("open the shop", ["shop"]),
        new("buy item 2 from the shop", ["shopbuy"]),
        new("set the welcome message", ["greetmsg"]),
        new("turn on welcome messages in this channel", ["greet"]),
        new("change the bot prefix to !", ["prefix"]),
        new("show the bot ping", ["ping"]),
        new("set me as afk", ["afk"]),
        new("show the last deleted message", ["snipe"]),
        new("go fishing", ["fish"]),
        new("show my waifu info", ["waifu"]),
        new("hug @friend", ["hug"]),
        new("add a todo item buy milk", ["todo add"]),
        new("show my todo list", ["todo list"]),
        new("set my timezone to Europe/Berlin", ["timezone"]),
        new("pick one: pizza or burger", ["choose"]),
        new("make the bot say hello", ["say"]),
        new("shorten this link", ["shorten"]),
        new("show the bot stats", ["stats"]),
        new("what commands are there for music", ["commands", "modules", "h"]),
        new("join the race", ["joinrace"]),
        new("deposit 500 into the bank", ["bank deposit"]),
        new("withdraw 200 from the bank", ["bank withdraw"]),
        new("leave my club", ["clubleave"]),
        new("show the club leaderboard", ["clublb"]),
        new("start a connect four game", ["connect4"]),
        new("play tic tac toe with @user", ["tictactoe"])
    ];

    // Written before the ranking change and never used to tune it, so it shows if a change only fits the cases above.
    public static IReadOnlyList<SearchCase> Holdout { get; } =
    [
        new("look up the meaning of the word ubiquitous", ["define"]),
        new("toss a coin for me", ["flip"]),
        new("throw a d6", ["roll"]),
        new("what's my currency balance", ["cash", "bank balance"]),
        new("send 50 flowers to @amy", ["give"]),
        new("set a reminder for tomorrow at 9", ["remind"]),
        new("permanently ban @griefer", ["ban"]),
        new("boot @user from the server", ["kick"]),
        new("silence @user for an hour", ["mute", "timeout", "chatmute"]),
        new("clear 50 messages", ["prune"]),
        new("queue up never gonna give you up", ["play", "queue"]),
        new("next track please", ["next"]),
        new("what's currently playing", ["nowplaying"]),
        new("time in new york", ["time"]),
        new("forecast for paris", ["weather"]),
        new("translate 'good morning' into japanese", ["translate"]),
        new("my level", ["experience"]),
        new("top players by xp", ["xpleaderboard"]),
        new("magic 8ball should i go out", ["eightball"]),
        new("daily reward", ["timely"]),
        new("TSLA share price", ["stock"]),
        new("ethereum price", ["crypto"]),
        new("find a youtube video of cats", ["youtube"]),
        new("profile picture of @user", ["avatar"]),
        new("random cat photo", ["randomcat"]),
        new("pokemon info for charizard", ["pokemon"]),
        new("wikipedia article on rome", ["wiki"]),
        new("how many inches in a meter", ["convert"]),
        new("what is 12 times 37", ["calculate"]),
        new("make a new role named Gamer", ["createrole"]),
        new("give the vip role to @x", ["setrole"]),
        new("slow down the chat", ["slowmode"]),
        new("start a game of trivia", ["trivia"]),
        new("play a game of blackjack", ["blackjack"]),
        new("buy something from the shop", ["shopbuy", "shop"]),
        new("what's the ping", ["ping"]),
        new("i'm going away, mark me afk", ["afk"]),
        new("who deleted that message", ["snipe"]),
        new("cast my fishing line", ["fish"]),
        new("remove my reminder", ["reminddelete"])
    ];

    // A word of the request which is also a short alias must not put that command among the 4 candidates.
    public static IReadOnlyList<SearchCase> Unrelated { get; } =
    [
        new("meet me at the park at 5", ["autotranslate"]),
        new("can we ban someone who spams", ["weather"]),
        new("pon la musica de bad bunny", ["linkallow"]),
        new("mi dinero, cuanto tengo", ["magicitem"]),
        new("se puede banear a alguien", ["showemojis"]),
        new("er hat mich beleidigt, verwarne ihn", ["emojiremove"]),
        new("lo quiero escuchar otra vez", ["local"]),
        new("np, thanks for the help", ["nowplaying"]),
        new("I've got a question about the bot", ["verboseerror"])
    ];

    public static SearchReport RunUnrelated(CommandSearchService search, int candidates)
    {
        var clean = 0;
        var hits = new List<SearchMiss>();
        foreach (var c in Unrelated)
        {
            var names = search.Search(c.Query, candidates).Select(static r => r.Command.Aliases[0]).ToArray();
            if (names.Any(c.Expected.Contains))
                hits.Add(new(c.Query, c.Expected, names));
            else
                clean++;
        }

        return new(Unrelated.Count, clean, clean, 0, hits, []);
    }

    public static IReadOnlyList<string> SmallTalk { get; } =
    [
        "yes", "no", "thanks", "thank you", "ok do it", "the second one", "hello", "lol", "what do you think?",
        "can you help me", "nevermind", "how are you", "nice", "why?", "who are you", "good bot", "hmm",
        "I don't know", "yeah that one", "cool, thanks!"
    ];

    // The best score for each labeled request and for each small talk text, to choose the candidate floor.
    public static string ScoreTable(CommandSearchService search)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("kind\tscore\ttop1\tok\ttext");
        foreach (var c in Cases.Concat(Holdout))
        {
            var r = search.Search(c.Query, 5);
            var top = r.Length > 0 ? r[0] : null;
            var ok = top is not null && c.Expected.Contains(top.Command.Aliases[0]);
            sb.AppendLine($"cmd\t{top?.Score:F3}\t{top?.Command.Aliases[0]}\t{ok}\t{c.Query}");
        }

        foreach (var t in SmallTalk)
        {
            var r = search.Search(t, 5);
            var top = r.Length > 0 ? r[0] : null;
            sb.AppendLine($"talk\t{top?.Score:F3}\t{top?.Command.Aliases[0]}\t-\t{t}");
        }

        return sb.ToString();
    }

    public static SearchReport Run(CommandSearchService search, IReadOnlyCollection<string> knownNames, IReadOnlyList<SearchCase> cases)
    {
        var unknown = new List<string>();
        foreach (var c in cases)
        {
            foreach (var e in c.Expected)
            {
                if (!knownNames.Contains(e))
                    unknown.Add(e);
            }
        }

        search.Search("warm up", 5);

        var top1 = 0;
        var top5 = 0;
        var misses = new List<SearchMiss>();
        var sw = Stopwatch.StartNew();
        foreach (var c in cases)
        {
            var results = search.Search(c.Query, 5);
            var names = results.Select(static r => r.Command.Aliases[0]).ToArray();

            if (names.Length > 0 && c.Expected.Contains(names[0]))
                top1++;
            else
                misses.Add(new(c.Query, c.Expected, names));

            if (names.Any(c.Expected.Contains))
                top5++;
        }

        sw.Stop();
        return new(cases.Count, top1, top5, sw.Elapsed.TotalMilliseconds / cases.Count, misses, unknown);
    }
}
