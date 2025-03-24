#nullable disable
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games;

namespace NadekoBot.Modules.Administration.Services;

public record struct NiceCatchNotifyModel(
    ulong UserId,
    FishData Fish,
    string Stars
) : INotifyModel<NiceCatchNotifyModel>
{
    public static string KeyName
        => "notify.nicecatch";

    public static NotifyType NotifyType
        => NotifyType.NiceCatch;

    public const string PH_EMOJI = "fish.emoji";
    public const string PH_IMAGE = "fish.image";
    public const string PH_NAME = "fish.name";
    public const string PH_STARS = "fish.stars";
    public const string PH_FLUFF = "fish.fluff";

    public bool TryGetUserId(out ulong userId)
    {
        userId = UserId;
        return true;
    }

    public static IReadOnlyList<NotifyModelPlaceholderData<NiceCatchNotifyModel>> GetReplacements()
    {
        return
        [
            new(PH_EMOJI, static (data, _) => data.Fish.Emoji),
            new(PH_IMAGE, static (data, _) => data.Fish.Image),
            new(PH_NAME, static (data, _) => data.Fish.Name),
            new(PH_STARS, static (data, _) => data.Stars),
            new(PH_FLUFF, static (data, _) => data.Fish.Fluff),
        ];
    }
}