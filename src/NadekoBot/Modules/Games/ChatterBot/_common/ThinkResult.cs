#nullable disable
namespace NadekoBot.Modules.Games.Common.ChatterBot;

public sealed class ThinkResult
{
    public string Text { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
}