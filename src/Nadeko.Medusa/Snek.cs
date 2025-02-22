using Discord;

namespace NadekoBot.Medusa;

/// <summary>
/// The base class which will be loaded as a module into NadekoBot
/// Any user-defined snek has to inherit from this class.
/// Sneks get instantiated ONLY ONCE during the loading,
/// and any snek commands will be executed on the same instance.
/// </summary>
public abstract class Snek : IAsyncDisposable
{
    /// <summary>
    /// Name of the snek. Defaults to the lowercase class name
    /// </summary>
    public virtual string Name
        => GetType().Name.ToLowerInvariant();

    /// <summary>
    /// The prefix required before the command name. For example
    /// if you set this to 'test' then a command called 'cmd' will have to be invoked by using
    /// '.test cmd' instead of `.cmd` 
    /// </summary>
    public virtual string Prefix
        => string.Empty;

    /// <summary>
    /// Executed once this snek has been instantiated and before any command is executed.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing completion</returns>
    public virtual ValueTask InitializeAsync()
        => default;

    /// <summary>
    /// Override to cleanup any resources or references which might hold this snek in memory
    /// </summary>
    /// <returns></returns>
    public virtual ValueTask DisposeAsync()
        => default;

    /// <summary>
    /// This method is called right after the message was received by the bot.
    /// You can use this method to make the bot conditionally ignore some messages and prevent further processing.
    /// <para>Execution order:</para>
    /// <para>
    /// *<see cref="ExecOnMessageAsync"/>* →
    /// <see cref="ExecInputTransformAsync"/> →
    /// <see cref="ExecPreCommandAsync"/> →
    /// <see cref="ExecPostCommandAsync"/> OR <see cref="ExecOnNoCommandAsync"/>
    /// </para>
    /// </summary>
    /// <param name="guild">Guild in which the message was sent</param>
    /// <param name="msg">Message received by the bot</param>
    /// <returns>A <see cref="ValueTask"/> representing whether the message should be ignored and not processed further</returns>
    public virtual ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
        => default;

    /// <summary>
    /// Override this method to modify input before the bot searches for any commands matching the input
    /// Executed after <see cref="ExecOnMessageAsync"/>
    /// This is useful if you want to reinterpret the message under some conditions
    /// <para>Execution order:</para>
    /// <para>
    /// <see cref="ExecOnMessageAsync"/> →
    /// *<see cref="ExecInputTransformAsync"/>* →
    /// <see cref="ExecPreCommandAsync"/> →
    /// <see cref="ExecPostCommandAsync"/> OR <see cref="ExecOnNoCommandAsync"/>
    /// </para> 
    /// </summary>
    /// <param name="guild">Guild in which the message was sent</param>
    /// <param name="channel">Channel in which the message was sent</param>
    /// <param name="user">User who sent the message</param>
    /// <param name="input">Content of the message</param>
    /// <returns>A <see cref="ValueTask"/> representing new, potentially modified content</returns>
    public virtual ValueTask<string?> ExecInputTransformAsync(
        IGuild? guild,
        IMessageChannel channel,
        IUser user,
        string input
    )
        => default;

    /// <summary>
    /// This method is called after the command was found but not executed,
    /// and can be used to prevent the command's execution.
    /// The command information doesn't have to be from this snek as this method
    /// will be called when *any* command from any module or snek was found.
    /// You can choose to prevent the execution of the command by returning "true" value.
    /// <para>Execution order:</para>
    /// <para>
    /// <see cref="ExecOnMessageAsync"/> →
    /// <see cref="ExecInputTransformAsync"/> →
    /// *<see cref="ExecPreCommandAsync"/>* →
    /// <see cref="ExecPostCommandAsync"/> OR <see cref="ExecOnNoCommandAsync"/>
    /// </para>
    /// </summary>
    /// <param name="context">Command context</param>
    /// <param name="moduleName">Name of the snek or module from which the command originates</param>
    /// <param name="commandName">Name of the command which is about to be executed</param>
    /// <returns>A <see cref="ValueTask"/> representing whether the execution should be blocked</returns>
    public virtual ValueTask<bool> ExecPreCommandAsync(
        AnyContext context,
        string moduleName,
        string commandName
    )
        => default;

    /// <summary>
    /// This method is called after the command was succesfully executed.
    /// If this method was called, then <see cref="ExecOnNoCommandAsync"/> will not be executed
    /// <para>Execution order:</para>
    /// <para>
    /// <see cref="ExecOnMessageAsync"/> →
    /// <see cref="ExecInputTransformAsync"/> →
    /// <see cref="ExecPreCommandAsync"/> →
    /// *<see cref="ExecPostCommandAsync"/>* OR <see cref="ExecOnNoCommandAsync"/>
    /// </para>
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing completion</returns>
    public virtual ValueTask ExecPostCommandAsync(AnyContext ctx, string moduleName, string commandName)
        => default;

    /// <summary>
    /// This method is called if no command was found for the input.
    /// Useful if you want to have games or features which take arbitrary input
    /// but ignore any messages which were blocked or caused a command execution
    /// If this method was called, then <see cref="ExecPostCommandAsync"/> will not be executed
    /// <para>Execution order:</para>
    /// <para>
    /// <see cref="ExecOnMessageAsync"/> →
    /// <see cref="ExecInputTransformAsync"/> →
    /// <see cref="ExecPreCommandAsync"/> →
    /// <see cref="ExecPostCommandAsync"/> OR *<see cref="ExecOnNoCommandAsync"/>*
    /// </para>
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing completion</returns>
    public virtual ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
        => default;
}

public readonly struct ExecResponse
{
}