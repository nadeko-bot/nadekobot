#nullable disable
namespace NadekoBot;

public sealed record SmartPlainText : SmartText
{
    public string Text { get; init; }

    public SmartPlainText(string text)
        => Text = text;

    public static implicit operator SmartPlainText(string input)
        => new(input);

    public static implicit operator string(SmartPlainText input)
        => input.Text;

    public override string ToString()
        => Text;
}