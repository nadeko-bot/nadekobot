using System.Collections.Frozen;

namespace NadekoBot.Common;

// Permission keys are group-qualified ("sar add"). These bare subcommand names were ambiguous
// across groups, so settings stored under them are removed rather than guessed at.
// Derived from data/commandlist.json, matching Migrations/20260730120000_permission-keys-qualified.sql
public static class CommandKeyMigration
{
    public static readonly FrozenSet<string> BareSubcommandKeys = FrozenSet.ToFrozenSet(
    [
        "ad", "add", "award", "balance", "cancel", "clear", "complete", "delete", "deposit", "done",
        "edit", "end", "error", "excl", "exclusive", "groupdelete", "groupname", "grouprolereq",
        "list", "ok", "pending", "rem", "remove", "removeall", "reroll", "rolelvlreq", "show",
        "start", "take", "uncomplete", "withdraw"
    ], StringComparer.InvariantCultureIgnoreCase);
}
