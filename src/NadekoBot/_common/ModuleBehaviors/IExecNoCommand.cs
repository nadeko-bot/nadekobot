namespace NadekoBot.Common.ModuleBehaviors;

/// <summary>
///     Executed if no command was found for this message
/// </summary>
public interface IExecNoCommand : IBehavior
{
    /// <summary>
    /// Executed at the end of the lifecycle if no command was found
    /// <see cref="IExecOnMessage"/> →
    /// <see cref="IInputTransformer"/> →
    /// <see cref="IExecPreCommand"/> →
    /// [<see cref="IExecPostCommand"/> | *<see cref="IExecNoCommand"/>*]
    /// </summary>
    /// <param name="guild"></param>
    /// <param name="msg"></param>
    /// <returns>A task representing completion</returns>
    Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg);
}