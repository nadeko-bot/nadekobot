using OneOf;

namespace NadekoBot.Common;

public interface IPermissionChecker
{
    Task<PermCheckResult> CheckPermsAsync(IGuild guild,
        IMessageChannel channel,
        IUser author,
        string module,
        string? cmd);
}

[GenerateOneOf]
public partial class PermCheckResult
    : OneOfBase<PermAllowed, PermCooldown, PermGlobalBlock, PermDisallowed>
{
    public bool IsAllowed
        => IsT0;
    
    public bool IsCooldown 
        => IsT1;
    
    public bool IsGlobalBlock 
        => IsT2;
    
    public bool IsDisallowed 
        => IsT3;
}

public readonly record struct PermAllowed;

public readonly record struct PermCooldown;

public readonly record struct PermGlobalBlock;

public readonly record struct PermDisallowed(int PermIndex, string PermText, bool IsVerbose);