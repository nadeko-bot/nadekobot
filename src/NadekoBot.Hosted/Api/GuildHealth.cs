using System.Runtime.InteropServices;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Hosted;

// The declaration order is the order of the features in the answer.
public enum HealthFeature : byte
{
    Greet,
    GreetDm,
    Bye,
    Boost,
    XpRewards,
    LevelUpNotify,
    WarnPunishments,
    AutoAssign,
    SelfAssign,
    ReactionRoles,
    ButtonRoles,
    Log,
    MuteRole,
    StreamNotify,
    Repeaters
}

// The declaration order is the order of the checks in the answer: problems first.
public enum CheckStatus : byte
{
    Broken,
    Warning,
    Off,
    Ok
}

public enum CheckIssue : byte
{
    None,
    ChannelMissing,
    ChannelNoAccess,
    RoleMissing,
    RoleAboveBot,
    RoleManaged,
    MissingPermission
}

public readonly record struct HealthRole(int Position, bool Managed, bool Everyone);

// The gateway cache of one guild, as the health checks see it. Tests give a fake.
public interface IHealthGuild
{
    GuildPermissions BotPermissions { get; }
    int BotHierarchy { get; }
    ulong? BotTopRoleId { get; }
    HealthRole? GetRole(ulong roleId);

    // Null when the guild has no text channel with this id.
    ChannelPermissions? GetChannelPermissions(ulong channelId);
}

public readonly struct SocketHealthGuild(SocketGuild guild, SocketGuildUser bot) : IHealthGuild
{
    public GuildPermissions BotPermissions
        => bot.GuildPermissions;

    public int BotHierarchy
        => bot.Hierarchy;

    // Discord breaks position ties by id, the lower id first.
    public ulong? BotTopRoleId
    {
        get
        {
            SocketRole? top = null;
            foreach (var role in bot.Roles)
            {
                if (role.IsEveryone)
                    continue;

                if (top is null || role.Position > top.Position || (role.Position == top.Position && role.Id < top.Id))
                    top = role;
            }

            return top?.Id;
        }
    }

    public HealthRole? GetRole(ulong roleId)
        => guild.GetRole(roleId) is { } role ? new HealthRole(role.Position, role.IsManaged, role.IsEveryone) : null;

    // The bot's services find their channels with GetTextChannel too, so voice chats and threads count.
    public ChannelPermissions? GetChannelPermissions(ulong channelId)
        => guild.GetTextChannel(channelId) is { } channel ? bot.GetPermissions(channel) : null;
}

public readonly record struct GreetSource(GreetType Type, bool Enabled, ulong? ChannelId, string? Message);

public readonly record struct PunishSource(PunishmentAction Action, ulong? RoleId);

public readonly record struct ChannelRoleSource(ulong ChannelId, ulong RoleId);

public readonly record struct RepeaterSource(ulong ChannelId, string? Message);

public sealed record HealthSources(
    GreetSource[] Greets,
    ulong[] XpRewardRoles,
    ulong?[] NotifyChannels,
    PunishSource[] Punishments,
    IReadOnlyList<ulong> AutoAssignRoles,
    ulong? MuteRoleId,
    ulong[] SelfAssignRoles,
    ChannelRoleSource[] ReactionRoles,
    ChannelRoleSource[] ButtonRoles,
    ulong[] LogChannels,
    ulong[] StreamChannels,
    RepeaterSource[] Repeaters)
{
    public static readonly HealthSources Empty = new([], [], [], [], [], null, [], [], [], [], [], []);
}

public static class GuildHealth
{
    public const int MAX_CHECKS = 200;

    private const int FEATURE_COUNT = (int)HealthFeature.Repeaters + 1;

    private static readonly string[] _featureNames =
    [
        "greet", "greetDm", "bye", "boost", "xpRewards", "levelUpNotify", "warnPunishments", "autoAssign",
        "selfAssign", "reactionRoles", "buttonRoles", "log", "muteRole", "streamNotify", "repeaters"
    ];

    private static readonly string[] _statusNames = ["broken", "warning", "off", "ok"];

    private static readonly string?[] _issueNames =
    [
        null, "channel_missing", "channel_no_access", "role_missing", "role_above_bot", "role_managed",
        "missing_permission"
    ];

    // The permissions which the dashboard knows, in the order of the answer.
    private static readonly GuildPermission[] _checkedPermissions =
    [
        GuildPermission.ViewChannel,
        GuildPermission.SendMessages,
        GuildPermission.EmbedLinks,
        GuildPermission.AttachFiles,
        GuildPermission.ReadMessageHistory,
        GuildPermission.AddReactions,
        GuildPermission.ManageMessages,
        GuildPermission.ManageRoles,
        GuildPermission.ManageChannels,
        GuildPermission.KickMembers,
        GuildPermission.BanMembers,
        GuildPermission.ModerateMembers,
        GuildPermission.MuteMembers
    ];

    private static readonly string[] _permissionNames = Array.ConvertAll(_checkedPermissions, static p => p.ToString());

    private enum EmbedNeed : byte
    {
        Required,
        Optional
    }

    public static GuildHealthDto Evaluate<TGuild>(in TGuild guild, HealthSources src, HealthTimersDto timers)
        where TGuild : struct, IHealthGuild
    {
        var checks = new CheckList();

        foreach (var greet in src.Greets)
        {
            if (!greet.Enabled || FeatureOf(greet.Type) is not { } feature)
                continue;

            if (greet.Type == GreetType.GreetDm)
            {
                checks.Add(new(feature, CheckStatus.Ok, CheckIssue.None, 0, null, null));
                continue;
            }

            var message = greet.Message ?? GreetService.GetDefaultGreet(greet.Type);
            AddChannelCheck(guild, ref checks, feature, greet.ChannelId, EmbedNeedOf(message));
        }

        foreach (var roleId in src.XpRewardRoles)
            AddRoleCheck(guild, ref checks, HealthFeature.XpRewards, roleId);

        // A notice without a channel goes to the channel of the event, which the bot used a moment earlier.
        foreach (var channelId in src.NotifyChannels)
        {
            if (channelId is null)
                checks.Add(new(HealthFeature.LevelUpNotify, CheckStatus.Ok, CheckIssue.None, 0, null, null));
            else
                AddChannelCheck(guild, ref checks, HealthFeature.LevelUpNotify, channelId, EmbedNeed.Required);
        }

        foreach (var p in src.Punishments)
            AddPunishmentCheck(guild, ref checks, p);

        foreach (var roleId in src.AutoAssignRoles)
            AddRoleCheck(guild, ref checks, HealthFeature.AutoAssign, roleId);

        foreach (var roleId in src.SelfAssignRoles)
            AddRoleCheck(guild, ref checks, HealthFeature.SelfAssign, roleId);

        // The bot gets no reaction events from a channel which it cannot see; a button click comes anyway.
        foreach (var rr in src.ReactionRoles)
            AddChannelRoleCheck(guild, ref checks, HealthFeature.ReactionRoles, rr, needsView: true);

        foreach (var br in src.ButtonRoles)
            AddChannelRoleCheck(guild, ref checks, HealthFeature.ButtonRoles, br, needsView: false);

        foreach (var channelId in src.LogChannels)
            AddChannelCheck(guild, ref checks, HealthFeature.Log, channelId, EmbedNeed.Required);

        // The bot makes a new mute role at the next mute, so a deleted one is not broken.
        if (src.MuteRoleId is { } muteRoleId)
        {
            var (status, issue, perm) = RoleStateInternal(guild, muteRoleId);
            if (issue == CheckIssue.RoleMissing)
                status = CheckStatus.Warning;

            checks.Add(new(HealthFeature.MuteRole, status, issue, perm, null, muteRoleId));
        }

        foreach (var channelId in src.StreamChannels)
            AddChannelCheck(guild, ref checks, HealthFeature.StreamNotify, channelId, EmbedNeed.Required);

        foreach (var rep in src.Repeaters)
            AddChannelCheck(guild, ref checks, HealthFeature.Repeaters, rep.ChannelId, EmbedNeedOf(rep.Message));

        return new GuildHealthDto(BotSectionInternal(guild), checks.Build(MAX_CHECKS), timers);
    }

    private static HealthBotDto BotSectionInternal<TGuild>(in TGuild guild)
        where TGuild : struct, IHealthGuild
    {
        var perms = guild.BotPermissions;
        var count = 0;
        if (!perms.Administrator)
        {
            foreach (var p in _checkedPermissions)
            {
                if (!perms.Has(p))
                    count++;
            }
        }

        string[] missing = count == 0 ? [] : new string[count];
        var i = 0;
        for (var j = 0; j < _checkedPermissions.Length && i < count; j++)
        {
            if (!perms.Has(_checkedPermissions[j]))
                missing[i++] = _permissionNames[j];
        }

        return new HealthBotDto(guild.BotTopRoleId?.ToString(), guild.BotHierarchy, perms.Administrator, missing);
    }

    private static HealthFeature? FeatureOf(GreetType type)
        => type switch
        {
            GreetType.Greet => HealthFeature.Greet,
            GreetType.GreetDm => HealthFeature.GreetDm,
            GreetType.Bye => HealthFeature.Bye,
            GreetType.Boost => HealthFeature.Boost,
            _ => null
        };

    // A plain message works without Embed Links, but Discord then shows no link previews.
    private static EmbedNeed EmbedNeedOf(string? message)
        => SmartText.CreateFrom(message ?? string.Empty).IsPlainText ? EmbedNeed.Optional : EmbedNeed.Required;

    private static void AddChannelCheck<TGuild>(
        in TGuild guild,
        ref CheckList checks,
        HealthFeature feature,
        ulong? channelId,
        EmbedNeed embed)
        where TGuild : struct, IHealthGuild
    {
        if (channelId is not { } id || guild.GetChannelPermissions(id) is not { } perms)
        {
            checks.Add(new(feature, CheckStatus.Broken, CheckIssue.ChannelMissing, 0, channelId, null));
            return;
        }

        var (status, perm) = SendAccessInternal(perms, embed);
        checks.Add(new(feature, status, status == CheckStatus.Ok ? CheckIssue.None : CheckIssue.ChannelNoAccess, perm, id, null));
    }

    private static (CheckStatus, GuildPermission) SendAccessInternal(ChannelPermissions perms, EmbedNeed embed)
    {
        if (!perms.ViewChannel)
            return (CheckStatus.Broken, GuildPermission.ViewChannel);

        if (!perms.SendMessages)
            return (CheckStatus.Broken, GuildPermission.SendMessages);

        if (!perms.EmbedLinks)
            return (embed == EmbedNeed.Required ? CheckStatus.Broken : CheckStatus.Warning, GuildPermission.EmbedLinks);

        return (CheckStatus.Ok, 0);
    }

    private static void AddRoleCheck<TGuild>(in TGuild guild, ref CheckList checks, HealthFeature feature, ulong? roleId)
        where TGuild : struct, IHealthGuild
    {
        var (status, issue, perm) = RoleStateInternal(guild, roleId);
        checks.Add(new(feature, status, issue, perm, null, roleId));
    }

    private static void AddChannelRoleCheck<TGuild>(
        in TGuild guild,
        ref CheckList checks,
        HealthFeature feature,
        ChannelRoleSource item,
        bool needsView)
        where TGuild : struct, IHealthGuild
    {
        if (guild.GetChannelPermissions(item.ChannelId) is not { } perms)
        {
            checks.Add(new(feature, CheckStatus.Broken, CheckIssue.ChannelMissing, 0, item.ChannelId, item.RoleId));
            return;
        }

        if (needsView && !perms.ViewChannel)
        {
            checks.Add(new(feature,
                CheckStatus.Broken,
                CheckIssue.ChannelNoAccess,
                GuildPermission.ViewChannel,
                item.ChannelId,
                item.RoleId));
            return;
        }

        var (status, issue, perm) = RoleStateInternal(guild, item.RoleId);
        checks.Add(new(feature, status, issue, perm, item.ChannelId, item.RoleId));
    }

    // The parts of RoleChecks.CanBotAssign, so that the dashboard can say which one fails.
    private static (CheckStatus, CheckIssue, GuildPermission) RoleStateInternal<TGuild>(in TGuild guild, ulong? roleId)
        where TGuild : struct, IHealthGuild
    {
        if (roleId is not { } id || guild.GetRole(id) is not { } role)
            return (CheckStatus.Broken, CheckIssue.RoleMissing, 0);

        if (role.Managed || role.Everyone)
            return (CheckStatus.Broken, CheckIssue.RoleManaged, 0);

        if (role.Position >= guild.BotHierarchy)
            return (CheckStatus.Broken, CheckIssue.RoleAboveBot, 0);

        if (!guild.BotPermissions.ManageRoles)
            return (CheckStatus.Broken, CheckIssue.MissingPermission, GuildPermission.ManageRoles);

        return (CheckStatus.Ok, CheckIssue.None, 0);
    }

    private static void AddPunishmentCheck<TGuild>(in TGuild guild, ref CheckList checks, PunishSource p)
        where TGuild : struct, IHealthGuild
    {
        if (p.Action == PunishmentAction.AddRole)
        {
            AddRoleCheck(guild, ref checks, HealthFeature.WarnPunishments, p.RoleId);
            return;
        }

        var required = UserPunishService.RequiredPermissions(p.Action);
        var perms = guild.BotPermissions;
        foreach (var perm in _checkedPermissions)
        {
            if ((required & perm) != 0 && !perms.Has(perm))
            {
                checks.Add(new(HealthFeature.WarnPunishments, CheckStatus.Broken, CheckIssue.MissingPermission, perm, null, null));
                return;
            }
        }

        checks.Add(new(HealthFeature.WarnPunishments, CheckStatus.Ok, CheckIssue.None, 0, null, null));
    }

    private static string? PermissionNameInternal(GuildPermission perm)
    {
        if (perm == 0)
            return null;

        var i = Array.IndexOf(_checkedPermissions, perm);
        return i < 0 ? perm.ToString() : _permissionNames[i];
    }

    private readonly record struct CheckKey(
        HealthFeature Feature,
        CheckStatus Status,
        CheckIssue Issue,
        GuildPermission Permission,
        ulong? ChannelId,
        ulong? RoleId);

    // Items with equal keys become one check. Permission is part of the key, so a kick step and a ban step stay apart.
    private struct CheckList()
    {
        private readonly Dictionary<CheckKey, int> _index = new();
        private readonly List<CheckKey> _keys = new();
        private readonly List<int> _counts = new();
        private int _seenFeatures;

        public void Add(CheckKey key)
        {
            _seenFeatures |= 1 << (int)key.Feature;
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_index, key, out var exists);
            if (exists)
            {
                _counts[slot]++;
                return;
            }

            slot = _keys.Count;
            _keys.Add(key);
            _counts.Add(1);
        }

        public HealthCheckDto[] Build(int max)
        {
            for (var f = 0; f < FEATURE_COUNT; f++)
            {
                if ((_seenFeatures & (1 << f)) != 0)
                    continue;

                _keys.Add(new((HealthFeature)f, CheckStatus.Off, CheckIssue.None, 0, null, null));
                _counts.Add(0);
            }

            var n = _keys.Count;
            var order = new (CheckStatus Status, HealthFeature Feature, int Index)[n];
            for (var i = 0; i < n; i++)
                order[i] = (_keys[i].Status, _keys[i].Feature, i);

            Array.Sort(order);

            var picked = new bool[n];
            var take = Math.Min(n, max);
            var taken = 0;

            // The worst check of each feature goes in first, so that a long list does not hide a feature.
            var featureTaken = 0;
            for (var i = 0; i < n && taken < take; i++)
            {
                var bit = 1 << (int)order[i].Feature;
                if ((featureTaken & bit) != 0)
                    continue;

                featureTaken |= bit;
                picked[i] = true;
                taken++;
            }

            for (var i = 0; i < n && taken < take; i++)
            {
                if (picked[i])
                    continue;

                picked[i] = true;
                taken++;
            }

            var result = new HealthCheckDto[taken];
            var r = 0;
            for (var i = 0; i < n; i++)
            {
                if (!picked[i])
                    continue;

                var idx = order[i].Index;
                var key = _keys[idx];
                result[r++] = new HealthCheckDto(
                    _featureNames[(int)key.Feature],
                    _statusNames[(int)key.Status],
                    _issueNames[(int)key.Issue],
                    PermissionNameInternal(key.Permission),
                    key.ChannelId?.ToString(),
                    key.RoleId?.ToString(),
                    _counts[idx]);
            }

            return result;
        }
    }
}
