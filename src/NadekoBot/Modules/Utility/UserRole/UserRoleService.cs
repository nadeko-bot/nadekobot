#nullable disable warnings
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Modules.Utility.UserRole;

public sealed class UserRoleService : IUserRoleService, INService
{
    private readonly DbService _db;
    private readonly IDiscordRoleManager _discordRoleManager;
    private readonly IHttpClientFactory _httpClientFactory;

    public UserRoleService(
        DbService db,
        IDiscordRoleManager discordRoleManager,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _discordRoleManager = discordRoleManager;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Assigns a role to a user and updates both database and Discord
    /// </summary>
    public async Task<bool> AddRoleAsync(ulong guildId, ulong userId, ulong roleId)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<UserRole>()
            .InsertOrUpdateAsync(() => new UserRole
                {
                    GuildId = guildId,
                    UserId = userId,
                    RoleId = roleId,
                },
                _ => new() { },
                () => new()
                {
                    GuildId = guildId,
                    UserId = userId,
                    RoleId = roleId
                });

        return true;
    }

    /// <summary>
    /// Removes a role from a user and updates both database and Discord
    /// </summary>
    public async Task<bool> RemoveRoleAsync(ulong guildId, ulong userId, ulong roleId)
    {
        await using var ctx = _db.GetDbContext();

        var deleted = await ctx.GetTable<UserRole>()
            .Where(r => r.GuildId == guildId && r.UserId == userId && r.RoleId == roleId)
            .DeleteAsync();

        return deleted > 0;
    }

    /// <summary>
    /// Gets all user roles for a guild
    /// </summary>
    public async Task<IReadOnlyCollection<UserRole>> ListRolesAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();

        var roles = await ctx.GetTable<UserRole>()
            .AsNoTracking()
            .Where(r => r.GuildId == guildId)
            .ToListAsyncLinqToDB();

        return roles;
    }

    /// <summary>
    /// Gets all roles for a specific user in a guild
    /// </summary>
    public async Task<IReadOnlyCollection<UserRole>> ListUserRolesAsync(ulong guildId, ulong userId)
    {
        await using var ctx = _db.GetDbContext();

        var roles = await ctx.GetTable<UserRole>()
            .AsNoTracking()
            .Where(r => r.GuildId == guildId && r.UserId == userId)
            .ToListAsyncLinqToDB();

        return roles;
    }

    /// <summary>
    /// Sets the custom color for a user's role and updates both database and Discord
    /// </summary>
    public async Task<bool> SetRoleColorAsync(ulong guildId, ulong userId, ulong roleId, Rgba32 color)
    {
        var discordSuccess = await _discordRoleManager.ModifyRoleAsync(
            guildId,
            roleId,
            color: color.ToDiscordColor());

        return discordSuccess;
    }

    /// <summary>
    /// Sets the custom name for a user's role and updates both database and Discord
    /// </summary>
    public async Task<bool> SetRoleNameAsync(ulong guildId, ulong userId, ulong roleId, string name)
    {
        var discordSuccess = await _discordRoleManager.ModifyRoleAsync(
            guildId,
            roleId,
            name: name);

        return discordSuccess;
    }

    /// <summary>
    /// Sets the custom icon for a user's role and updates both database and Discord
    /// </summary>
    public async Task<bool> SetRoleIconAsync(ulong guildId, ulong userId, ulong roleId, string iconUrl)
    {
        // Validate the URL format
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
            return false;

        try
        {
            // Download the image
            using var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

            // Check if the response is successful
            if (!response.IsSuccessStatusCode)
                return false;

            // Check content type - must be image/png or image/jpeg
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower();
            if (contentType != "image/png"
                && contentType != "image/jpeg"
                && contentType != "image/webp")
                return false;

            // Check file size - Discord limit is 256KB
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > 256 * 1024)
                return false;

            // Save the image to a memory stream
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Create Discord image from stream
            using var discordImage = new Image(memoryStream);

            // Upload the image to Discord
            var discordSuccess = await _discordRoleManager.ModifyRoleAsync(
                guildId,
                roleId,
                image: discordImage);

            return discordSuccess;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process role icon from URL {IconUrl}", iconUrl);
            return false;
        }
    }

    /// <summary>
    /// Checks if a user has a specific role assigned
    /// </summary>
    public async Task<bool> UserOwnsRoleAsync(ulong guildId, ulong userId, ulong roleId)
    {
        await using var ctx = _db.GetDbContext();

        return await ctx.GetTable<UserRole>()
            .AnyAsyncLinqToDB(r => r.GuildId == guildId && r.UserId == userId && r.RoleId == roleId);
    }
}