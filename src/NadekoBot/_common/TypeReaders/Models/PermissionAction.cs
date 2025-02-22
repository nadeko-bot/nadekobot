#nullable disable
namespace NadekoBot.Common.TypeReaders.Models;

public class PermissionAction
{
    public static PermissionAction Enable
        => new(true);

    public static PermissionAction Disable
        => new(false);

    public bool Value { get; }

    public PermissionAction(bool value)
        => Value = value;

    public override bool Equals(object obj)
    {
        if (obj is null || GetType() != obj.GetType())
            return false;

        return Value == ((PermissionAction)obj).Value;
    }

    public override int GetHashCode()
        => Value.GetHashCode();
}