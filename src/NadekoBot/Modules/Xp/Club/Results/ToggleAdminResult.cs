namespace NadekoBot.Modules.Xp.Services;

public enum ToggleAdminResult
{
    AddedAdmin,
    RemovedAdmin,
    NotOwner,
    TargetNotMember,
    CantTargetThyself,
}