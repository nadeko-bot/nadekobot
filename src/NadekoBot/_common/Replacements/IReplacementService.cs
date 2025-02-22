namespace NadekoBot.Common;

public interface IReplacementService
{
    ValueTask<string?> ReplaceAsync(string input, ReplacementContext repCtx);
    ValueTask<SmartText> ReplaceAsync(SmartText input, ReplacementContext repCtx);
}