namespace NadekoBot.Common.ModuleBehaviors;

/// <summary>
/// Implemented by services which may transform input before a command is searched for
/// </summary>
public interface IInputTransformer : IBehavior
{
    /// <summary>
    /// Ran after a non-bot message was received
    /// <see cref="IExecOnMessage"/> ->
    /// *<see cref="IInputTransformer"/>* ->
    /// <see cref="IExecPreCommand"/> ->
    /// [<see cref="IExecPostCommand"/> OR <see cref="IExecNoCommand"/>]
    /// </summary>
    /// <param name="guild">Guild</param>
    /// <param name="channel">Channel in which the message was sent</param>
    /// <param name="user">User who sent the message</param>
    /// <param name="input">Content of the message</param>
    /// <returns>New input, if any, otherwise null</returns>
    Task<string?> TransformInput(
        IGuild guild,
        IMessageChannel channel,
        IUser user,
        string input);
}