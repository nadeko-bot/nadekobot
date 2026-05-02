#nullable disable
using NadekoBot.Modules.Games.Services;
using System.Text;

namespace NadekoBot.Modules.Games;

/* more games
- Shiritori
- Simple RPG adventure
*/
public partial class Games : NadekoModule<GamesService>
{
    private readonly IImageCache _images;
    private readonly IHttpClientFactory _httpFactory;

    public Games(IImageCache images, IHttpClientFactory factory)
    {
        _images = images;
        _httpFactory = factory;
    }

    [Cmd]
    public async Task Choose([Leftover] string list = null)
    {
        if (string.IsNullOrWhiteSpace(list))
            return;
        var listArr = list.Split(';');
        if (listArr.Length < 2)
            return;
        var rng = new NadekoRandom();
        await Response().Confirm("🤔", listArr[rng.Next(0, listArr.Length)]).SendAsync();
    }

    [Cmd]
    public async Task EightBall([Leftover] string question = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            return;

        var res = _service.GetEightballResponse(ctx.User.Id, question);
        await Response()
              .Embed(CreateEmbed()
                     .WithOkColor()
                     .WithDescription(ctx.User.ToString())
                     .AddField("❓ " + GetText(strs.question), question)
                     .AddField("🎱 " + GetText(strs._8ball), res))
              .SendAsync();
    }

    private readonly string[] _numberEmojis = ["0️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣"];

    [Cmd]
    public async Task Minesweeper(int mines = 12)
    {
        var boardSizeX = 9;
        var boardSizeY = 10;

        if (mines < 1)
        {
            mines = 1;
        }
        else if (mines > boardSizeX * boardSizeY / 2)
        {
            mines = boardSizeX * boardSizeY / 2;
        }

        var mineIndicies = Enumerable.Range(0, boardSizeX * boardSizeY)
                                     .ToArray()
                                     .Shuffle()
                                     .Take(mines)
                                     .ToHashSet();

        string GetNumberOnCell(int x, int y)
        {
            var count = 0;
            for (var i = -1; i < 2; i++)
            {
                for (var j = -1; j < 2; j++)
                {
                    if (y + j >= boardSizeY || y + j < 0)
                        continue;
                    if (x + i >= boardSizeX || x + i < 0)
                        continue;

                    var boardIndex = (y + j) * boardSizeX + (x + i);
                    if (mineIndicies.Contains(boardIndex))
                        count++;
                }
            }

            return _numberEmojis[count];
        }

        var sb = new StringBuilder();
        sb.AppendLine($"### Minesweeper [{mines}\\💣]");
        for (var i = 0; i < boardSizeY; i++)
        {
            for (var j = 0; j < boardSizeX; j++)
            {
                var emoji = mineIndicies.Contains((i * boardSizeX) + j) ? "💣" : GetNumberOnCell(j, i);
                sb.Append($"||{emoji}||");
            }

            sb.AppendLine();
        }

        await Response().Text(sb.ToString()).SendAsync();
    }
}