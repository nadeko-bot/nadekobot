using NadekoBot.Modules.Administration;

namespace NadekoBot.Common;

// The command runs only from a message which the user typed. The bot refuses it when it runs the command on
// someone's behalf (AI agent, alias, scheduled command, shop item, .doas), and the AI agent's command search leaves it out.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoAiAgentAttribute : PreconditionAttribute
{
    public const string REQUIREMENT = "No AI Agent";

    public override Task<PreconditionResult> CheckPermissionsAsync(
        ICommandContext context,
        CommandInfo command,
        IServiceProvider services)
        => Task.FromResult(context.Message is DoAsUserMessage
            ? PreconditionResult.FromError("This command runs only from a message which you typed.")
            : PreconditionResult.FromSuccess());
}
