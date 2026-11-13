namespace NadekoBot.Modules.Gambling;

public readonly record struct RewardBreakdown(long Amount, string Bonuses, string QuestLine, bool DailiesDone);

public static class RewardMessage
{
    // 4 rows of 5 links keep the reward layout under the limit of 40 components in a message
    public const int MAX_LINK_ROWS = 4;

    // Each link is "Label|URL".
    public static List<ActionRowBuilder> CreateLinkRows(ReadOnlySpan<string> links)
    {
        var rows = new List<ActionRowBuilder>();
        ActionRowBuilder? row = null;
        foreach (var link in links)
        {
            var sep = link.IndexOf('|');
            if (sep <= 0 || sep == link.Length - 1)
                continue;

            if (row is null || row.Components.Count == ActionRowBuilder.MaxChildCount)
            {
                if (rows.Count == MAX_LINK_ROWS)
                    break;

                row = new ActionRowBuilder();
                rows.Add(row);
            }

            row.WithButton(link[..sep], style: ButtonStyle.Link, url: link[(sep + 1)..]);
        }

        return rows;
    }

    public static ComponentBuilderV2 Build(string header, in RewardBreakdown reward, Color? accent, ButtonBuilder questButton)
    {
        var questText = new TextDisplayBuilder(reward.QuestLine);

        var container = new ContainerBuilder()
            .WithAccentColor(accent)
            .WithTextDisplay(new TextDisplayBuilder(header + "\n\n" + reward.Bonuses));

        if (reward.DailiesDone)
            container.WithTextDisplay(questText);
        else
            container.WithSection(new SectionBuilder().WithTextDisplay(questText).WithAccessory(questButton));

        return new ComponentBuilderV2().WithContainer(container);
    }
}
