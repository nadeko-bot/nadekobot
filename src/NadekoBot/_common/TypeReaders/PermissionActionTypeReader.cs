#nullable disable
using NadekoBot.Common.TypeReaders.Models;

namespace NadekoBot.Common.TypeReaders;

/// <summary>
///     Used instead of bool for more flexible keywords for true/false only in the permission module
/// </summary>
public sealed class PermissionActionTypeReader : NadekoTypeReader<PermissionAction>
{
    public override ValueTask<TypeReaderResult<PermissionAction>> ReadAsync(ICommandContext context, string input)
    {
        input = input.ToUpperInvariant();
        switch (input)
        {
            case "1":
            case "T":
            case "TRUE":
            case "ENABLE":
            case "ENABLED":
            case "ALLOW":
            case "PERMIT":
            case "UNBAN":
                return new(TypeReaderResult.FromSuccess(PermissionAction.Enable));
            case "0":
            case "F":
            case "FALSE":
            case "DENY":
            case "DISABLE":
            case "DISABLED":
            case "DISALLOW":
            case "BAN":
                return new(TypeReaderResult.FromSuccess(PermissionAction.Disable));
            default:
                return new(TypeReaderResult.FromError<PermissionAction>(CommandError.ParseFailed,
                    "Did not receive a valid boolean value"));
        }
    }
}