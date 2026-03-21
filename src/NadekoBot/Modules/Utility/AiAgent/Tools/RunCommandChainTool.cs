using System.Text;
using System.Text.Json;
using Discord.WebSocket;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Executes a sequence of Nadeko commands with a configurable delay between each.
/// Max 5 commands, delay clamped to 2000-10000ms.
/// </summary>
public sealed class RunCommandChainTool(ICommandHandler cmdHandler) : IAiTool, INService
{
    private const int MAX_COMMANDS = 5;
    private const int MIN_DELAY_MS = 2000;
    private const int MAX_DELAY_MS = 10000;
    private const int DEFAULT_DELAY_MS = 2000;

    public string Name => "run_command_chain";

    public string Description =>
        "Execute a sequence of Nadeko bot commands in order, with a delay between each. " +
        $"Maximum {MAX_COMMANDS} commands per chain. Delay is {MIN_DELAY_MS}-{MAX_DELAY_MS}ms (default {DEFAULT_DELAY_MS}ms). " +
        "Each command goes through the full permission pipeline. " +
        "If a command fails, the chain continues with the remaining commands. " +
        "Use search_commands first to find the right commands and their syntax.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse($$"""
        {
            "type": "object",
            "properties": {
                "commands": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "List of command strings including prefix, e.g. ['.mute @user 10m', '.warn @user reason']. Max {{MAX_COMMANDS}} commands."
                },
                "delay_ms": {
                    "type": "integer",
                    "description": "Delay in milliseconds between each command (default {{DEFAULT_DELAY_MS}}, min {{MIN_DELAY_MS}}, max {{MAX_DELAY_MS}})"
                }
            },
            "required": ["commands"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("commands", out var cmdsEl)
            || cmdsEl.ValueKind != JsonValueKind.Array)
            return "Error: commands must be an array of command strings.";

        var commands = cmdsEl.EnumerateArray()
            .Select(e => e.GetString()?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (commands.Count == 0)
            return "Error: At least one command is required.";

        if (commands.Count > MAX_COMMANDS)
            return $"Error: Maximum {MAX_COMMANDS} commands per chain.";

        var delay = DEFAULT_DELAY_MS;
        if (arguments.TryGetProperty("delay_ms", out var delayEl) && delayEl.TryGetInt32(out var d))
            delay = Math.Clamp(d, MIN_DELAY_MS, MAX_DELAY_MS);

        if (context.Guild is not SocketGuild sg)
            return "Error: Commands can only be executed in a server.";

        if (context.SourceChannel is not ISocketMessageChannel ch)
            return "Error: Invalid channel context.";

        var ct = context.CancellationToken;
        var results = new StringBuilder();

        for (var i = 0; i < commands.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                results.AppendLine($"Chain cancelled after {i}/{commands.Count} commands.");
                break;
            }

            if (i > 0)
                await Task.Delay(delay, ct);

            var cmd = commands[i]!;
            try
            {
                var fakeMessage = new DoAsUserMessage(context.TriggerMessage, context.User, cmd);
                var task = cmdHandler.TryRunCommand(sg, ch, fakeMessage);
                var completed = await Task.WhenAny(task, Task.Delay(3000, ct));
                if (completed == task && task.IsFaulted)
                    results.AppendLine($"[{i + 1}] Failed: {cmd} - {task.Exception?.InnerException?.Message}");
                else
                    results.AppendLine($"[{i + 1}] Executed: {cmd}");
            }
            catch (Exception ex)
            {
                results.AppendLine($"[{i + 1}] Failed: {cmd} - {ex.Message}");
            }
        }

        return results.ToString();
    }
}
