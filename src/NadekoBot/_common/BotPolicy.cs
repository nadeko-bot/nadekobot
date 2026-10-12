using NadekoBot.Modules.Administration;

namespace NadekoBot.Common;

// Managed bots run on a host which the bot owner does not control, so the owner cannot load code or read local files.
public sealed record BotPolicy(bool Managed)
{
    // The host decides these settings for a managed bot. A null prop locks every prop of the config.
    private static readonly (string Config, string? Prop)[] _managedLocks =
    [
        ("bot", "checkforupdates"),
        ("bot", "console.type"),
        ("patron", "enabled"),
        ("music", "maxcachesizegb"),
        ("searches", "webEngine"),
        ("searches", "imgEngine"),
        ("searches", "ytProvider"),
    ];

    // The owner of a self-hosted bot owns its network.
    public bool CanReachLocalNetwork(IBotCreds creds, IMessage message)
        => !Managed && IsRealOwner(creds, message);

    // Text which someone else wrote (AI agent, shop item, alias, scheduled command) never counts as the owner.
    public static bool IsRealOwner(IBotCreds creds, IMessage message)
        => message is not DoAsUserMessage { BlocksOwnerCommands: true }
           && creds.IsOwner(message.Author);

    public bool IsLocked(string config, string prop)
    {
        if (!Managed)
            return false;

        // Config and prop names are ASCII identifiers.
        foreach (var (lockedConfig, lockedProp) in _managedLocks)
        {
            if (string.Equals(lockedConfig, config, StringComparison.OrdinalIgnoreCase)
                && (lockedProp is null || string.Equals(lockedProp, prop, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}
