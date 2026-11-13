using System.Text;

namespace NadekoBot.Modules.Utility.AiAgent;

// A Components V2 message keeps its text in text displays, not in the content or an embed.
public static class LayoutText
{
    public static string Read(IReadOnlyCollection<IMessageComponent>? components)
    {
        if (components is not { Count: > 0 })
            return string.Empty;

        StringBuilder? sb = null;
        AppendInternal(ref sb, components);
        return sb?.ToString() ?? string.Empty;
    }

    private static void AppendInternal(ref StringBuilder? sb, IReadOnlyCollection<IMessageComponent>? components)
    {
        if (components is null)
            return;

        foreach (var component in components)
        {
            switch (component)
            {
                case TextDisplayComponent { Content.Length: > 0 } text:
                    sb ??= new();
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append(text.Content);
                    break;
                case ContainerComponent container:
                    AppendInternal(ref sb, container.Components);
                    break;
                case SectionComponent section:
                    AppendInternal(ref sb, section.Components);
                    break;
            }
        }
    }
}
