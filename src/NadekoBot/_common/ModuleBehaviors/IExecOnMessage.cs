namespace NadekoBot.Common.ModuleBehaviors;

/// <summary>
///     Implemented by modules to handle non-bot messages received
/// </summary>
public interface IExecOnMessage : IBehavior
{
    int Priority { get; }

    /// <summary>
    /// Ran after a non-bot message was received
    /// *<see cref="IExecOnMessage"/>* →
    /// <see cref="IInputTransformer"/> →
    /// <see cref="IExecPreCommand"/> →
    /// [<see cref="IExecPostCommand"/> | <see cref="IExecNoCommand"/>]
    /// </summary>
    /// <param name="guild">Guild where the message was sent</param>
    /// <param name="msg">The message that was received</param>
    /// <returns>Whether further processing of this message should be blocked</returns>
    Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg);
}