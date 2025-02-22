using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Services;

public interface IGuildColorsService
{
    Colors? GetColors(ulong guildId);
    Task SetOkColor(ulong guildId, Rgba32? color);
    Task SetErrorColor(ulong guildId, Rgba32? color);
    Task SetPendingColor(ulong guildId, Rgba32? color);
}

public record struct Colors(Color? Ok, Color? Warn, Color? Error);