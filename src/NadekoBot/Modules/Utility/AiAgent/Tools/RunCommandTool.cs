using System.Text.Json;
using Discord.WebSocket;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Executes a Nadeko bot command as the invoking user.
/// Goes through the full command pipeline - permissions, cooldowns, and checks all apply.
/// </summary>
public sealed class RunCommandTool(ICommandHandler cmdHandler) : IAiTool, INService
{
    public string Name => "run_command";

    public string Description =>
        "Execute a Nadeko bot command as the user who invoked the agent. " +
        "The command string must include the prefix (e.g. '.mute @user 10m'). " +
        "All permission checks apply - the command will fail if the user lacks permission. " +
        "Use search_commands first to find the right command and its syntax.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "The full command string including prefix, e.g. '.mute @user 10m reason'"
                }
            },
            "required": ["command"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("command", out var cmdEl)
            || string.IsNullOrWhiteSpace(cmdEl.GetString()))
            return "Error: command is required.";

        var commandText = cmdEl.GetString()!.Trim();

        if (context.Guild is not SocketGuild sg)
            return "Error: Commands can only be executed in a server.";

        if (context.SourceChannel is not ISocketMessageChannel ch)
            return "Error: Invalid channel context.";

        var fakeMessage = new DoAsUserMessage(context.TriggerMessage, context.User, commandText);

        try
        {
            var task = cmdHandler.TryRunCommand(sg, ch, fakeMessage);
            var completed = await Task.WhenAny(task, Task.Delay(3000, context.CancellationToken));
            if (completed == task && task.IsFaulted)
                return $"Error executing command: {task.Exception?.InnerException?.Message}";
            return $"Command executed: {commandText}";
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }
}
