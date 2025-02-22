namespace NadekoBot.Common.ModuleBehaviors;

/// <summary>
/// This interface's method is executed after a command was found but before it was executed.
/// Able to block further processing of a command
/// </summary>
public interface IExecPreCommand : IBehavior
{
    public int Priority { get; }

    /// <summary>
    /// <para>
    /// Ran after a command was found but before execution.
    /// </para>
    /// <see cref="IExecOnMessage"/> →
    /// <see cref="IInputTransformer"/> →
    /// *<see cref="IExecPreCommand"/>* →
    /// [<see cref="IExecPostCommand"/> | <see cref="IExecNoCommand"/>]
    /// </summary>
    /// <param name="context">Command context</param>
    /// <param name="moduleName">Name of the module</param>
    /// <param name="command">Command info</param>
    /// <returns>Whether further processing of the command is blocked</returns>
    Task<bool> ExecPreCommandAsync(ICommandContext context, string moduleName, CommandInfo command);
}