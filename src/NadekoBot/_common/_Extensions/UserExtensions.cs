using NadekoBot.Db.Models;

namespace NadekoBot.Extensions;

public static class UserExtensions
{
    // This method is used by everything that fetches the avatar from a user
    public static Uri RealAvatarUrl(this IUser usr, ushort size = 256)
        => usr.AvatarId is null ? new(usr.GetDefaultAvatarUrl()) : new Uri(usr.GetAvatarUrl(ImageFormat.Auto, size));

    // This method is only used for the xp card
    public static Uri? RealAvatarUrl(this DiscordUser usr)
    {
        if (!string.IsNullOrWhiteSpace(usr.AvatarId))
            return new Uri(CDN.GetUserAvatarUrl(usr.UserId, usr.AvatarId, 128, ImageFormat.Png));

        return Uri.TryCreate(CDN.GetDefaultUserAvatarUrl(usr.UserId), UriKind.Absolute, out var uri)
            ? uri
            : null;
    }
}