using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Games;

public interface INCanvasService
{
    Task<uint[]> GetCanvas();
    Task<NCPixel[]> GetPixelGroup(int position);

    Task<SetPixelResult> SetPixel(
        int position,
        uint color,
        string text,
        ulong userId,
        long price);

    Task<bool> SetImage(uint[] img);

    Task<NCPixel?> GetPixel(int x, int y);
    Task<NCPixel?> GetPixel(int position);
    int GetHeight();
    int GetWidth();
    Task ResetAsync();
}