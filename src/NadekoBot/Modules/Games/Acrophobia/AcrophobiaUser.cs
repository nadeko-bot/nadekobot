#nullable disable
namespace NadekoBot.Modules.Games.Common.Acrophobia;

public class AcrophobiaUser
{
    public string UserName { get; }
    public ulong UserId { get; }
    public string Input { get; }

    public AcrophobiaUser(ulong userId, string userName, string input)
    {
        UserName = userName;
        UserId = userId;
        Input = input;
    }

    public override int GetHashCode()
        => UserId.GetHashCode();

    public override bool Equals(object obj)
        => obj is AcrophobiaUser x ? x.UserId == UserId : false;
}