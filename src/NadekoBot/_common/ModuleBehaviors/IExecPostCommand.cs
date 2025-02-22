namespace NadekoBot.Common.ModuleBehaviors;

/// <summary>
/// This interface's method is executed after the command successfully finished execution.
/// ***There is no support for this method in NadekoBot services.***
/// It is only meant to be used in medusa system
/// </summary>
public interface IExecPostCommand : IBehavior
{
    /// <summary>
    /// Executed after a command was successfully executed
    /// <see cref="IExecOnMessage"/> →
    /// <see cref="IInputTransformer"/> →
    /// <see cref="IExecPreCommand"/> →
    /// [*<see cref="IExecPostCommand"/>* | <see cref="IExecNoCommand"/>]
    /// </summary>
    /// <param name="ctx">Command context</param>
    /// <param name="moduleName">Module name</param>
    /// <param name="commandName">Command name</param>
    /// <returns>A task representing completion</returns>
    ValueTask ExecPostCommandAsync(ICommandContext ctx, string moduleName, string commandName);
}