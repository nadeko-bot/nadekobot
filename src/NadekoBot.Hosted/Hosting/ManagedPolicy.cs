using DryIoc;

namespace NadekoBot.Hosted;

public static class ManagedPolicy
{
    public static void Register(IContainer services)
        => services.RegisterInstance(new BotPolicy(true), IfAlreadyRegistered.Replace);
}
