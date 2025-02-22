// ReSharper disable InconsistentNaming
#nullable disable
namespace NadekoBot.Modules.Utility;

public class EvalGlobals
{
    public ICommandContext ctx;
    public Utility self;
    public IUser user;
    public IMessageChannel channel;
    public IGuild guild;
    public IServiceProvider services;
}