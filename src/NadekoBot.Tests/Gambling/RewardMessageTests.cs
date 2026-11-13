#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using NadekoBot.Modules.Gambling;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Modules.Utility.AiAgent;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public sealed class RewardMessageTests
{
    [Test]
    public void RewardLayout_ShowsTheQuestButtonUntilTheDailiesAreDone_PutsLinksInRowsOfFive_AndTheAgentReadsIt()
    {
        var button = QuestLogService.CreateButtonWithLabel("Daily quests");

        var open = RewardMessage
            .Build("You voted!", new RewardBreakdown(100, "**100** base reward", "❌ quests", false), Color.Blue, button)
            .Build();
        var done = RewardMessage
            .Build("You voted!", new RewardBreakdown(150, "**100** base reward", "✅ quests", true), Color.Blue, button)
            .Build();

        Assert.That(open.Components.First().Type, Is.EqualTo(ComponentType.Container));
        Assert.That(ButtonIds(open.Components), Is.EqualTo(new[] { "n:quests" }));
        Assert.That(ButtonIds(done.Components), Is.Empty);

        Assert.That(LayoutText.Read(open.Components), Is.EqualTo("You voted!\n\n**100** base reward\n❌ quests"));
        Assert.That(LayoutText.Read(done.Components), Does.EndWith("base reward\n✅ quests"));

        string[] links = ["No link|", "|https://example.com/x", .. Enumerable.Range(0, 23).Select(static i => $"Site {i}|https://example.com/{i}")];
        var rows = RewardMessage.CreateLinkRows(links);
        Assert.That(rows.Select(static r => r.Components.Count), Is.EqualTo(Enumerable.Repeat(5, RewardMessage.MAX_LINK_ROWS)));
        Assert.That(RewardMessage.CreateLinkRows(links.AsSpan(0, 2)), Is.Empty);

        var full = RewardMessage.Build("You voted!", new RewardBreakdown(100, "**100** base reward", "❌ quests", false), Color.Blue, button);
        foreach (var row in rows)
            full.WithActionRow(row);
        full.WithActionRow(new ActionRowBuilder().WithButton("Remind me", "timely:remind_me"));
        Assert.DoesNotThrow(() => full.Build());
    }

    private static List<string> ButtonIds(IEnumerable<IMessageComponent> components)
    {
        var ids = new List<string>();
        foreach (var component in components)
        {
            switch (component)
            {
                case ButtonComponent button:
                    ids.Add(button.CustomId);
                    break;
                case ContainerComponent container:
                    ids.AddRange(ButtonIds(container.Components));
                    break;
                case SectionComponent section:
                    ids.AddRange(ButtonIds([section.Accessory, .. section.Components]));
                    break;
                case ActionRowComponent row:
                    ids.AddRange(ButtonIds(row.Components));
                    break;
            }
        }

        return ids;
    }
}
