#nullable enable
using NadekoBot.Common;
using NadekoBot.Modules.Patronage;
using NadekoBot.Modules.Xp;
using NadekoBot.Modules.Xp.Services;
using NUnit.Framework;
using YamlDotNet.Core;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ConfigUpgradeTests
{
    private static readonly YamlSeria _seria = new();

    [Test]
    public void XpConfig_FromV4AndV5_KeepsTheTextXpSettings()
    {
        // 4.x and early 5.x: cooldown in minutes
        var v4 = Upgrade("""
                         version: 5
                         xpPerMessage: 7
                         messageXpCooldown: 2
                         xpFromImage: 0
                         voiceXpPerMinute: 1.5
                         """);
        Assert.That((v4.TextXpPerMessage, v4.TextXpCooldown, v4.TextXpFromImage, v4.VoiceXpPerMinute),
            Is.EqualTo((7, 120, 0, 2)));

        // late 5.x: cooldown in seconds
        var v5 = Upgrade("""
                         version: 7
                         xpPerMessage: 4
                         messageXpCooldown: 45
                         xpFromImage: 9
                         voiceXpPerMinute: 0
                         """);
        Assert.That((v5.TextXpPerMessage, v5.TextXpCooldown, v5.TextXpFromImage, v5.VoiceXpPerMinute),
            Is.EqualTo((4, 45, 9, 0)));

        var both = Upgrade("""
                           version: 10
                           textXpPerMessage: 5
                           xpPerMessage: 99
                           voiceXpPerMinute: 2.5
                           """);
        Assert.That((both.TextXpPerMessage, both.VoiceXpPerMinute), Is.EqualTo((5, 3)));

        const string current = """
                               version: 11
                               textXpPerMessage: 5
                               xpPerMessage: 99
                               """;
        Assert.That(XpConfigService.UpgradeLegacyYaml(current), Is.EqualTo(current));

        static XpConfig Upgrade(string yaml)
            => _seria.Deserialize<XpConfig>(XpConfigService.UpgradeLegacyYaml(yaml));
    }

    [Test]
    public void PatronConfig_FromV4_LoadsWithoutTheOldQuotas()
    {
        const string v4 = """
                          version: 2
                          isEnabled: true
                          quotas:
                            features:
                              timely:extra_percent:
                                V: 10
                            commands:
                              google:
                                V:
                                  PerDay: 15
                          """;
        Assert.That(() => _seria.Deserialize<PatronConfigData>(v4), Throws.InstanceOf<YamlException>());

        var upgraded = _seria.Deserialize<PatronConfigData>(PatronageConfig.UpgradeLegacyYaml(v4));
        Assert.That(upgraded.IsEnabled, Is.True);
        Assert.That(upgraded.Quotas, Is.Empty);

        var current = _seria.Deserialize<PatronConfigData>(PatronageConfig.UpgradeLegacyYaml("""
            version: 3
            quotas:
              V:
                ai: 50
            """));
        Assert.That(current.Quotas[PatronTier.V]["ai"], Is.EqualTo(50));

        const string noVersion = """
                                 quotas:
                                   V:
                                     ai: 50
                                 """;
        Assert.That(PatronageConfig.UpgradeLegacyYaml(noVersion), Is.EqualTo(noVersion));
    }
}
