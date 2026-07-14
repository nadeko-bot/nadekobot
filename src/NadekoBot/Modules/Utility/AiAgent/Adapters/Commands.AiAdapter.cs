using System.Text;
using Discord.WebSocket;
using NadekoBot.AiAgent;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed class CommandsAiAdapter(ICommandHandler cmdHandler) : IAiCoreToolGroup, INService
{
    public string GroupName => "commands";
    public string GroupDescription => "Run Nadeko commands as the user (single command or short chain).";

    private const int CHAIN_MAX_COMMANDS = 5;
    private const int CHAIN_MIN_DELAY_MS = 2000;
    private const int CHAIN_MAX_DELAY_MS = 10000;
    private const int CHAIN_DEFAULT_DELAY_MS = 2000;
    private const int COMMAND_TIMEOUT_MS = 3000;

    [AiTool(
        "run_command",
        "Execute a Nadeko bot command as the user who invoked the agent. "
        + "Provide the command WITHOUT a prefix (e.g. 'mute @user 10m'); the bot adds the "
        + "server's configured prefix automatically. "
        + "All permission checks apply - the command will fail if the user lacks permission. "
        + "Use search_commands first to find the right command and its syntax. "
        + "Returns immediately after dispatch; the command's output (embed/text) appears in "
        + "channel_history on the next turn, authored by the bot. The user already sees it. "
        + "Use it to verify the command worked or to compose a follow-up tool call; "
        + "do NOT restate it in your final reply.")]
    [AiSystemGuidance(SystemGuidanceText.RunCommand)]
    public async Task<string> RunCommand(
        AiToolContext ctx,
        [AiParam("The command string WITHOUT a prefix, e.g. 'mute @user 10m reason'")]
        string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw ToolException.InvalidArgument("command is required.");

        command = command.Trim();

        if (ctx.Guild is not SocketGuild sg)
            throw ToolException.Forbidden("Commands can only be executed in a server.");
        if (ctx.SourceChannel is not ISocketMessageChannel ch)
            throw ToolException.Forbidden("Invalid channel context.");

        command = EnsurePrefixInternal(command, sg);

        var fakeMessage = new DoAsUserMessage(ctx.TriggerMessage, ctx.User, command);

        try
        {
            var task = cmdHandler.TryRunCommand(sg, ch, fakeMessage);
            var completed = await Task.WhenAny(task, Task.Delay(COMMAND_TIMEOUT_MS, ctx.CancellationToken));
            if (completed == task && task.IsFaulted)
                return $"Error executing command: {task.Exception?.InnerException?.Message}";
            return $"Command executed: {command}";
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    [AiTool(
        "run_command_chain",
        "Execute a sequence of Nadeko bot commands in order, with a delay between each. "
        + "Maximum 5 commands per chain. Delay is 2000-10000ms (default 2000ms). "
        + "Provide each command WITHOUT a prefix; the bot adds the server's prefix automatically. "
        + "Each command goes through the full permission pipeline. "
        + "If a command fails, the chain continues with the remaining commands. "
        + "Use search_commands first to find the right commands and their syntax. "
        + "Each command's output appears in channel_history on subsequent turns, authored "
        + "by the bot. The user already sees them; do NOT restate them in your final reply.")]
    public async Task<string> RunCommandChain(
        AiToolContext ctx,
        [AiParam("List of command strings WITHOUT a prefix, e.g. ['mute @user 10m', 'warn @user reason']. Max 5.")]
        List<string> commands,
        [AiParam("Delay in milliseconds between each command (default 2000, min 2000, max 10000)")]
        int delayMs = CHAIN_DEFAULT_DELAY_MS)
    {
        if (commands is null || commands.Count == 0)
            throw ToolException.InvalidArgument("At least one command is required.");

        if (commands.Count > CHAIN_MAX_COMMANDS)
            throw ToolException.InvalidArgument($"Maximum {CHAIN_MAX_COMMANDS} commands per chain.");

        delayMs = Math.Clamp(delayMs, CHAIN_MIN_DELAY_MS, CHAIN_MAX_DELAY_MS);

        if (ctx.Guild is not SocketGuild sg)
            throw ToolException.Forbidden("Commands can only be executed in a server.");
        if (ctx.SourceChannel is not ISocketMessageChannel ch)
            throw ToolException.Forbidden("Invalid channel context.");

        var ct = ctx.CancellationToken;
        var sb = new StringBuilder();

        for (var i = 0; i < commands.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                sb.AppendLine($"Chain cancelled after {i}/{commands.Count} commands.");
                break;
            }

            if (i > 0)
                await Task.Delay(delayMs, ct);

            var cmd = commands[i]?.Trim();
            if (string.IsNullOrWhiteSpace(cmd))
            {
                sb.AppendLine($"[{i + 1}] Skipped: empty command");
                continue;
            }

            try
            {
                cmd = EnsurePrefixInternal(cmd, sg);
                var fakeMessage = new DoAsUserMessage(ctx.TriggerMessage, ctx.User, cmd);
                var task = cmdHandler.TryRunCommand(sg, ch, fakeMessage);
                var completed = await Task.WhenAny(task, Task.Delay(COMMAND_TIMEOUT_MS, ct));
                if (completed == task && task.IsFaulted)
                    sb.AppendLine($"[{i + 1}] Failed: {cmd} - {task.Exception?.InnerException?.Message}");
                else
                    sb.AppendLine($"[{i + 1}] Executed: {cmd}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[{i + 1}] Failed: {cmd} - {ex.Message}");
            }
        }

        return sb.ToString();
    }

    private string EnsurePrefixInternal(string command, SocketGuild guild)
    {
        var prefix = cmdHandler.GetPrefix(guild);
        return command.StartsWith(prefix, StringComparison.InvariantCulture)
            ? command
            : prefix + command;
    }
}
