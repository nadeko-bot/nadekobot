namespace NadekoBot.Services;

public enum CommandRunStatus
{
    Ran,
    NotFound,
    BadArguments,
    Refused,
    Error,

    // Another feature of the bot took the message before the command parser, for example a word filter.
    Intercepted
}

public readonly record struct CommandRunResult(CommandRunStatus Status, string? Reason = null, CommandInfo? Command = null);
