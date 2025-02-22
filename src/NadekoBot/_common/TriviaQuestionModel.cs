#nullable disable
namespace NadekoBot.Modules.Games.Common.Trivia;

public sealed class TriviaQuestionModel
{
    public string Category { get; init; }
    public string Question { get; init; }
    public string ImageUrl { get; init; }
    public string AnswerImageUrl { get; init; }
    public string Answer { get; init; }
}