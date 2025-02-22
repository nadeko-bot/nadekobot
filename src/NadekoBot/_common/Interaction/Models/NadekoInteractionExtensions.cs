namespace NadekoBot;

public static class NadekoInteractionExtensions
{
    public static MessageComponent CreateComponent(
        this NadekoInteractionBase nadekoInteractionBase
    )
    {
        var cb = new ComponentBuilder();

        nadekoInteractionBase.AddTo(cb);

        return cb.Build();
    }
}