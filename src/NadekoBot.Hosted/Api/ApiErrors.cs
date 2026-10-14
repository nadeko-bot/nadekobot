using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted;

public static class ApiErrors
{
    public const string BAD_REQUEST = "bad_request";
    public const string UNAUTHORIZED = "unauthorized";
    public const string NOT_FOUND = "not_found";
    public const string METHOD_NOT_ALLOWED = "method_not_allowed";
    public const string CONFLICT = "conflict";
    public const string TOO_LARGE = "too_large";
    public const string UNSUPPORTED_MEDIA_TYPE = "unsupported_media_type";
    public const string RATE_LIMITED = "rate_limited";
    public const string INTERNAL_ERROR = "internal_error";

    public const string NOT_READY = "not_ready";
    public const string WRONG_SHARD = "wrong_shard";
    public const string INVALID_VALUE = "invalid_value";
    public const string PROP_NOT_FOUND = "prop_not_found";
    public const string SETTING_LOCKED = "setting_locked";
    public const string CHANNEL_NOT_FOUND = "channel_not_found";
    public const string CHANNEL_REQUIRED = "channel_required";
    public const string USER_NOT_FOUND = "user_not_found";
    public const string ROLE_NOT_FOUND = "role_not_found";
    public const string MISSING_PERMISSIONS = "missing_permissions";
    public const string TOO_MANY_STREAMS = "too_many_streams";
    public const string DISCORD_UNAVAILABLE = "discord_unavailable";
    public const string LIMIT_REACHED = "limit_reached";

    public const string NAME_TAKEN = "name_taken";
    public const string BACKUP_IN_PROGRESS = "backup_in_progress";
    public const string IMPORT_IN_PROGRESS = "import_in_progress";
    public const string RESTORE_IN_PROGRESS = "restore_in_progress";
    public const string DELETE_IN_PROGRESS = "delete_in_progress";
    public const string BACKUP_UNSUPPORTED = "backup_unsupported";
    public const string BACKUP_FAILED = "backup_failed";
    public const string RESTORE_UNSUPPORTED = "restore_unsupported";
    public const string NO_SPACE = "no_space";
    public const string NOT_A_ZIP = "not_a_zip";
    public const string MISSING_DB = "missing_db";
    public const string NOT_SQLITE = "not_sqlite";
    public const string UNSAFE_ENTRY = "unsafe_entry";
    public const string ARCHIVE_TOO_LARGE = "archive_too_large";

    public const string UPDATE_IN_PROGRESS = "update_in_progress";
    public const string CHECKSUM_MISMATCH = "checksum_mismatch";
    public const string BAD_PACKAGE = "bad_package";
    public const string VERSION_MISMATCH = "version_mismatch";
    public const string UPDATE_TRIAL_PENDING = "update_trial_pending";

    public const string DISK_UNAVAILABLE = "disk_unavailable";

    public const string SERVER_IMPORT_IN_PROGRESS = "server_import_in_progress";
    public const string BAD_EXPORT = "bad_export";
    public const string GUILD_MISMATCH = "guild_mismatch";
    public const string EXPORT_TOO_NEW = "export_too_new";
    public const string SCHEMA_MISMATCH = "schema_mismatch";
    public const string IMPORT_FAILED = "import_failed";

    public static readonly IResult NotFound = Create(NOT_FOUND, StatusCodes.Status404NotFound);
    public static readonly IResult InvalidValue = Create(INVALID_VALUE, StatusCodes.Status400BadRequest);
    public static readonly IResult ChannelNotFound = Create(CHANNEL_NOT_FOUND, StatusCodes.Status400BadRequest);
    public static readonly IResult ChannelRequired = Create(CHANNEL_REQUIRED, StatusCodes.Status400BadRequest);
    public static readonly IResult UserNotFound = Create(USER_NOT_FOUND, StatusCodes.Status404NotFound);
    public static readonly IResult RoleNotFound = Create(ROLE_NOT_FOUND, StatusCodes.Status400BadRequest);
    public static readonly IResult PropNotFound = Create(PROP_NOT_FOUND, StatusCodes.Status404NotFound);
    public static readonly IResult SettingLocked = Create(SETTING_LOCKED, StatusCodes.Status403Forbidden);
    public static readonly IResult MissingPermissions = Create(MISSING_PERMISSIONS, StatusCodes.Status400BadRequest);
    public static readonly IResult DiscordUnavailable = Create(DISCORD_UNAVAILABLE, StatusCodes.Status502BadGateway);
    public static readonly IResult Unauthorized = Create(UNAUTHORIZED, StatusCodes.Status401Unauthorized);
    public static readonly IResult LimitReached = Create(LIMIT_REACHED, StatusCodes.Status409Conflict);

    public static IResult Create(string code, int status)
        => Results.Json(new ErrorDto(code), ApiJsonContext.Default.ErrorDto, statusCode: status);

    public static Task WriteAsync(HttpContext http, int status, string code)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new ErrorDto(code), ApiJsonContext.Default.ErrorDto);
    }

    public static string CodeFor(int status)
        => status switch
        {
            StatusCodes.Status401Unauthorized => UNAUTHORIZED,
            StatusCodes.Status404NotFound => NOT_FOUND,
            StatusCodes.Status405MethodNotAllowed => METHOD_NOT_ALLOWED,
            StatusCodes.Status409Conflict => CONFLICT,
            StatusCodes.Status413PayloadTooLarge => TOO_LARGE,
            StatusCodes.Status415UnsupportedMediaType => UNSUPPORTED_MEDIA_TYPE,
            StatusCodes.Status422UnprocessableEntity => INVALID_VALUE,
            StatusCodes.Status429TooManyRequests => RATE_LIMITED,
            >= StatusCodes.Status500InternalServerError => INTERNAL_ERROR,
            _ => BAD_REQUEST
        };
}
