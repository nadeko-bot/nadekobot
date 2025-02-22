namespace NadekoBot.Services;

public interface IImageCache
{
    Task<byte[]?> GetHeadsImageAsync();
    Task<byte[]?> GetTailsImageAsync();
    Task<byte[]?> GetCurrencyImageAsync();
    Task<byte[]?> GetXpBackgroundImageAsync();
    Task<byte[]?> GetDiceAsync(int num);
    Task<byte[]?> GetSlotEmojiAsync(int number);
    Task<byte[]?> GetSlotBgAsync();
    Task<byte[]?> GetImageDataAsync(Uri url);
}