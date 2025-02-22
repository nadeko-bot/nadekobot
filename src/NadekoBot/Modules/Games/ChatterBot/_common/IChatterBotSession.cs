#nullable disable
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Games.Common.ChatterBot;

public interface IChatterBotSession
{
    Task<OneOf<ThinkResult, Error<string>>> Think(string input, string username);
}