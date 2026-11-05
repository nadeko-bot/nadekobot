using System.Collections.Frozen;
using System.Net;
using Discord.Net;

namespace NadekoBot.Modules.Administration;

public enum ProfileImage
{
    Avatar,
    Banner
}

public enum ProfileStatus
{
    Success,
    NoImage,
    BadImage,
    TooLong,
    Wait,
    MissingPerm,
    RateLimited,
    Refused
}

// Value is the character limit for TooLong and the seconds to wait for Wait.
public readonly record struct ProfileChange(ProfileStatus Status, int Value = 0);

public sealed class BotProfileService(
    DiscordSocketClient client,
    IHttpClientFactory httpFactory,
    IBotCache cache) : INService
{
    public const int MAX_IMAGE_MB = 8;
    public const int MAX_USERNAME_LENGTH = 32;
    public const int MAX_NICKNAME_LENGTH = 32;
    public const int MAX_BIO_LENGTH = 190;

    public const int SERVER_COOLDOWN_SECONDS = 30;

    private const int MAX_IMAGE_BYTES = MAX_IMAGE_MB * 1024 * 1024;
    private static readonly TimeSpan _serverCooldown = TimeSpan.FromSeconds(SERVER_COOLDOWN_SECONDS);

    private static readonly FrozenSet<string> _imageTypes = FrozenSet.ToFrozenSet(
        ["image/png", "image/jpeg", "image/gif", "image/webp"],
        StringComparer.OrdinalIgnoreCase);

    private static TypedKey<DateTime> CooldownKey(ulong guildId)
        => new($"botprofile:{guildId}");

    public async Task<ProfileChange> SetGlobalImageAsync(IAttachment? attachment, ProfileImage kind)
    {
        if (attachment is null)
            return new(ProfileStatus.NoImage);

        if (!IsAllowedImage(attachment))
            return new(ProfileStatus.BadImage);

        var bytes = await DownloadInternalAsync(attachment);
        if (bytes is null)
            return new(ProfileStatus.BadImage);

        using var stream = new MemoryStream(bytes, false);
        var image = new Image(stream);
        return await ModifyInternalAsync(() => client.CurrentUser.ModifyAsync(u =>
        {
            if (kind == ProfileImage.Avatar)
                u.Avatar = image;
            else
                u.Banner = image;
        }));
    }

    // A null attachment resets the server image to the global one.
    public async Task<ProfileChange> SetServerImageAsync(IGuild guild, IAttachment? attachment, ProfileImage kind)
    {
        if (attachment is not null && !IsAllowedImage(attachment))
            return new(ProfileStatus.BadImage);

        return await RunServerChangeInternalAsync(guild.Id,
            async () =>
            {
                byte[]? bytes = null;
                if (attachment is not null)
                {
                    bytes = await DownloadInternalAsync(attachment);
                    if (bytes is null)
                        return new(ProfileStatus.BadImage);
                }

                using var stream = bytes is null ? null : new MemoryStream(bytes, false);
                Image? image = stream is null ? null : new Image(stream);
                return await ModifyInternalAsync(() => guild.ModifyCurrentUserAsync(x =>
                {
                    if (kind == ProfileImage.Avatar)
                        x.Avatar = image;
                    else
                        x.Banner = image;
                }));
            });
    }

    public async Task<ProfileChange> SetGlobalNameAsync(string name)
    {
        name = name.Trim();
        if (name.Length > MAX_USERNAME_LENGTH)
            return new(ProfileStatus.TooLong, MAX_USERNAME_LENGTH);

        return await ModifyInternalAsync(() => client.CurrentUser.ModifyAsync(u => u.Username = name));
    }

    // A blank nickname removes the nickname.
    public async Task<ProfileChange> SetServerNicknameAsync(IGuild guild, string? nickname)
    {
        nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
        if (nickname?.Length > MAX_NICKNAME_LENGTH)
            return new(ProfileStatus.TooLong, MAX_NICKNAME_LENGTH);

        var me = await guild.GetCurrentUserAsync();
        if (!me.GuildPermissions.ChangeNickname)
            return new(ProfileStatus.MissingPerm);

        return await RunServerChangeInternalAsync(guild.Id,
            () => ModifyInternalAsync(() => me.ModifyAsync(u => u.Nickname = nickname)));
    }

    // A blank bio clears the bio.
    public async Task<ProfileChange> SetServerBioAsync(IGuild guild, string? bio)
    {
        bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        if (bio?.Length > MAX_BIO_LENGTH)
            return new(ProfileStatus.TooLong, MAX_BIO_LENGTH);

        return await RunServerChangeInternalAsync(guild.Id,
            () => ModifyInternalAsync(() => guild.ModifyCurrentUserAsync(x => x.Bio = bio)));
    }

    private static bool IsAllowedImage(IAttachment attachment)
        => attachment.Size <= MAX_IMAGE_BYTES
           && attachment.ContentType is { } type
           && _imageTypes.Contains(type);

    private async Task<byte[]?> DownloadInternalAsync(IAttachment attachment)
    {
        using var http = httpFactory.CreateClient(ExternalHttp.CLIENT_NAME);
        try
        {
            return await http.GetBytesLimitedAsync(attachment.Url, MAX_IMAGE_BYTES);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warning(ex, "Unable to download a profile image attachment");
            return null;
        }
    }

    // The slot is taken before the download, so only one change runs for each server.
    // A failed change frees it at once, except a rate limit, where a retry only hits the limit again.
    private async Task<ProfileChange> RunServerChangeInternalAsync(ulong guildId, Func<Task<ProfileChange>> change)
    {
        var key = CooldownKey(guildId);
        var until = DateTime.UtcNow + _serverCooldown;
        if (!await cache.AddAsync(key, until, _serverCooldown, overwrite: false))
        {
            var current = await cache.GetAsync(key);
            var wait = current.TryPickT0(out var end, out _) ? end - DateTime.UtcNow : _serverCooldown;
            return new(ProfileStatus.Wait, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)));
        }

        var result = new ProfileChange(ProfileStatus.Refused);
        try
        {
            result = await change();
            return result;
        }
        finally
        {
            if (result.Status is not (ProfileStatus.Success or ProfileStatus.RateLimited))
                await cache.RemoveAsync(key);
        }
    }

    private static async Task<ProfileChange> ModifyInternalAsync(Func<Task> modify)
    {
        try
        {
            await modify();
            return new(ProfileStatus.Success);
        }
        catch (RateLimitedException)
        {
            return new(ProfileStatus.RateLimited);
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.TooManyRequests)
        {
            return new(ProfileStatus.RateLimited);
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            Log.Warning("Discord refused a bot profile change: {Code} {Reason}", ex.HttpCode, ex.Reason);
            return new(ProfileStatus.Refused);
        }
    }
}
