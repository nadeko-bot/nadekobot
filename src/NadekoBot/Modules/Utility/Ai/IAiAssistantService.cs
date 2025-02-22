using OneOf;

namespace NadekoBot.Modules.Utility;

public interface IAiAssistantService
{
    Task<OneOf<NadekoCommandCallModel, GetCommandErrorResult>> TryGetCommandAsync(
        ulong userId,
        string prompt,
        IReadOnlyCollection<AiCommandModel> commands,
        string prefix);

    IReadOnlyCollection<AiCommandModel> GetCommands();

    Task<bool> TryExecuteAiCommand(
        IGuild guild,
        IUserMessage msg,
        ITextChannel channel,
        string query);
}