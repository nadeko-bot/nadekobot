namespace NadekoBot.Modules.Xp.Services;

public enum ClubRenameResult
{
    NotOwnerOrAdmin,
    Success,
    NameTaken,
    NameTooLong
}