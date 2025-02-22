#nullable disable warnings
using Cloneable;

namespace NadekoBot.Modules.Xp.Services;

[Cloneable]
public sealed partial class UserXpGainData : ICloneable<UserXpGainData>
{
    public IGuildUser User { get; init; }
    public IGuild Guild { get; init; }
    public IMessageChannel Channel { get; init; }
    public int XpAmount { get; set; }
}