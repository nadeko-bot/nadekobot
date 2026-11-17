using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility.AiAgent;

// The agent runs commands through this, so an offline evaluation can replace the command handler.
public interface IAgentCommandRunner
{
    // The text as the model wrote it, or without a prefix.
    AgentCommandMatch? Find(IGuild guild, string prefix, string text);

    Task<CommandRunResult> RunAsync(IGuild guild, ITextChannel channel, DoAsUserMessage message);
}

// Name and Args are what the model asked for (first alias or alias trigger); Target and TargetArgs are what really runs.
public readonly record struct AgentCommandMatch(
    string Name,
    string Args,
    bool SpendsCurrency,
    string Content,
    string Target,
    string TargetArgs,
    IReadOnlyList<string> Aliases);

public sealed class AgentCommandRunner(ICommandHandler cmdHandler, CommandService commands, AliasService aliases)
    : IAgentCommandRunner, INService
{
    public AgentCommandMatch? Find(IGuild guild, string prefix, string text)
    {
        var prefixed = prefix + text;

        // The command handler rewrites an alias before it looks for a command, so an alias with the prefix hides a command.
        if (FindAliasInternal(guild, prefix, prefixed) is { } shadow)
            return shadow;

        if (FindCommandInternal(text) is { } cmd)
            return new(cmd.Name, cmd.Args, cmd.Spends, prefixed, cmd.Name, cmd.Args, cmd.Aliases);

        return FindAliasInternal(guild, prefix, text);
    }

    private AgentCommandMatch? FindAliasInternal(IGuild guild, string prefix, string content)
    {
        if (aliases.FindAlias(guild, content) is not { } alias
            || !alias.Resolved.StartsWith(prefix, StringComparison.Ordinal)
            || FindCommandInternal(alias.Resolved[prefix.Length..].TrimStart()) is not { } target)
            return null;

        var trigger = alias.Trigger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? alias.Trigger[prefix.Length..]
            : alias.Trigger;
        var args = alias.Trigger.Length < content.Length
            ? content.AsSpan(alias.Trigger.Length).Trim().ToString()
            : "";

        return new(trigger, args, target.Spends, content, target.Name, target.Args, [trigger, ..target.Aliases]);
    }

    private (string Name, string Args, bool Spends, IReadOnlyList<string> Aliases)? FindCommandInternal(string text)
    {
        var result = commands.Search(text);
        if (!result.IsSuccess || result.Commands.Count == 0)
            return null;

        // Each overload is one match, and an alias of a group can be longer than the alias of a command.
        var best = result.Commands[0];
        var spends = false;
        foreach (var match in result.Commands)
        {
            if (match.Alias.Length > best.Alias.Length)
                best = match;

            spends |= SpendsCurrencyAttribute.IsMarked(match.Command);
        }

        var args = best.Alias.Length < text.Length
            ? text.AsSpan(best.Alias.Length).Trim().ToString()
            : "";

        return (best.Command.Aliases[0], args, spends, best.Command.Aliases);
    }

    public Task<CommandRunResult> RunAsync(IGuild guild, ITextChannel channel, DoAsUserMessage message)
    {
        if (guild is not SocketGuild sg || channel is not ISocketMessageChannel ch)
            throw new InvalidOperationException("Commands can only be executed in a server text channel.");

        return cmdHandler.TryRunCommand(sg, ch, message);
    }
}
