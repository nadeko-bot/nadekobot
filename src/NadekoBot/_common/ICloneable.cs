#nullable disable
namespace NadekoBot.Common;

public interface ICloneable<T>
    where T : new()
{
    public T Clone();
}