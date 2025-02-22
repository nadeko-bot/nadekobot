using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Services;

public interface ICustomBehavior
    : IExecOnMessage,
        IInputTransformer,
        IExecPreCommand,
        IExecNoCommand,
        IExecPostCommand
{

}